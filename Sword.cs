using System.Collections;
using UnityEngine;

public class Sword : Weapon
{
    #region State Variables
    // Action states for the sword
    enum SwordActions
    {
        SlashBuild,
        Slashing,
        Stabbing, 
        CoolingDown,
        Waiting
    }

    // Current action state of the sword
    private SwordActions currentAction = SwordActions.Waiting;

    // Is the sword busy performing an action?
    public override bool Busy 
    { 
        get 
        { 
            return currentAction != SwordActions.Waiting; 
        } 
    }
    #endregion

    #region Cooldown Variables
    [Header("Cooldowns")]
    [SerializeField, Tooltip("Time until next fire available after slash")]
    private float _cooldownSlash = 0.9f;
    [SerializeField, Tooltip("Time until next fire available after stab")]
    private float _cooldownStab = 0.5f;

    [SerializeField, Tooltip("Time from cooldown start player can click to immediately execute an action on cooldown end.")]
    private float _queueTimeFromEndOfCooldown = .05f;

    // Time duration of the currently active cooldown
    private float _currentCooldownDuration = 0f;
    // Time a cooldown began at, for tracking if a click should be queued
    private float _cooldownStartedTime = -1f;
    #endregion

    #region Slashing Attack Variables
    [Header("Slashing attack")]
    // Range of slash attack, will match collider of slashObj
    private float _slashRange = 4f;
    [SerializeField, Tooltip("Opening angle of slash")]
    private float _slashBaseAngle = 100f;
    [SerializeField, Tooltip("Speed of the slash")]
    private float _slashBaseSpeed = 500f;
    [SerializeField, Tooltip("Base damage of the slash before modifiers")]
    private float _slashBaseDamage = 1f;

    [SerializeField, Tooltip("Time to hold to start slash modification")]
    private float _slashMinHoldTime = 3f;
    [SerializeField, Tooltip("Time to hold to get max modifiers on a slash")]
    private float _slashMaxHoldTime = 3f;
    [SerializeField, Tooltip("Max angle of the slash after modifiers")]
    private float _slashMaxAngle = 120f;
    [SerializeField, Tooltip("Max speed of the slash after modifiers")]
    private float _slashMaxSpeed = 700f;
    [SerializeField, Tooltip("Max damage of the slash after modifiers")]
    private float _slashMaxDamage = 2f;

    // Time slash action began at, useful for tracking modifier values
    private float _timeSlashBegan = -1f;

    [SerializeField, Tooltip("Parent object of the slash")]
    private GameObject slashObj;

    // Slash object handles damaging enemies during sweep
    private DamageWithinTrigger _slashDamageWithinTrigger;

    // Is the slash button currently being held?
    private bool _isSlashHeld = false;

    // Should another slash be executed as soon as the previous cooldown ends?
    private bool _isSlashQueued = false;
    #endregion

    #region Stabbing Attack Variables
    [Header("Stabbing attack")]
    [SerializeField, Tooltip("Range of the stab attack")]
    private float _stabRange = 5f;
    [SerializeField, Tooltip("Breadth of the stab damage")]
    private float _stabBreadth = 1f;
    [SerializeField, Tooltip("Speed of the stab")]
    private float _stabBaseSpeed = 500f;
    [SerializeField, Tooltip("Base damage of stab without modifiers")]
    private float _stabBaseDamage = 2f;
    [SerializeField, Tooltip("Parent object of the stab")]
    private GameObject stabObj;

    // Should another stab be executed as soon as the previous cooldown ends?
    private bool _isStabQueued = false;
    #endregion

    private void Awake()
    {
        slashObj.SetActive(false);
        stabObj.SetActive(false);

        // Slash range should match the length of the slashing collider
        _slashRange = slashObj.GetComponentInChildren<Collider2D>().bounds.size.x;

        _slashDamageWithinTrigger = slashObj.GetComponentInChildren<DamageWithinTrigger>();
    }

    public override void Fire()
    {
        if (currentAction == SwordActions.CoolingDown)
        {
            // Is not attempting is build a slash, close enough to the end of a cooldown to queue up an action
            if (!_isSlashHeld && CanQueueAction())
            {
                // Stab action is queued
                _isStabQueued = true;
                // Unqueue slash action 
                _isSlashQueued = false;
            }

            return;
        }
        else if (Busy)
        {
            return;
        }

        Stab();
    }

    public override void SecondaryFire()
    {
        _isSlashHeld = true;

        if(currentAction == SwordActions.CoolingDown)
        {
            // Close enough to end of a cooldown to queue up an action
            if (CanQueueAction())
            {
                _isStabQueued = false;
                _isSlashQueued = true;
            }

            return;
        }
        else if (Busy)
        {
            return;
        }

        SlashStart();
    }

    public override void FireReleased() 
    {
        // Stab does not require released behaviour
    }

    public override void SecondaryFireReleased()
    {
        _isSlashHeld = false;

        if (currentAction == SwordActions.SlashBuild)
        {
            SlashRelease();
        }
    }

    /// <summary>
    /// Abort current action and cleanup timers
    /// </summary>
    public override void AbortCurrentAction()
    {
        switch(currentAction)
        {
            case SwordActions.SlashBuild:
                CancelInvoke(nameof(SlashBuild));
                currentAction = SwordActions.Waiting;
                break;
            default:
                currentAction = SwordActions.Waiting;
                break;
        }
    }

    /// <summary>
    /// Start slash tracking
    /// </summary>
    private void SlashStart()
    {
        currentAction = SwordActions.SlashBuild;

        _timeSlashBegan = Time.time;

        Invoke(nameof(SlashBuild), _slashMinHoldTime);
    }

    /// <summary>
    /// Start building slash modifiers
    /// </summary>
    private void SlashBuild()
    {
        if (currentAction != SwordActions.SlashBuild)
        {
            return;
        }

        // Trigger visual indication
        PlayerAnimations.Instance.BuildSlash();
    }

    /// <summary>
    /// Slash input released, slash
    /// </summary>
    private void SlashRelease()
    {
        currentAction = SwordActions.Slashing;

        PlayerController.instance.SetMovementBlock(true, "Sword");

        // Cancel slash build update in case it was not triggered
        CancelInvoke(nameof(SlashBuild));

        float damage = GetHeldAdditive(_timeSlashBegan, _slashMinHoldTime, _slashMaxHoldTime, _slashBaseDamage, _slashMaxDamage) + _slashBaseDamage;
        _slashDamageWithinTrigger.SetDamage(damage);

        Vector2 direction = AttackDirection();
        float angleOpening = GetHeldAdditive(_timeSlashBegan, _slashMinHoldTime, _slashMaxHoldTime, _slashBaseAngle, _slashMaxAngle) + _slashBaseAngle;

        // Initial rotation of sword
        float angle = Mathf.Atan2(direction.y, direction.x);
        angle -= (angleOpening / 2 * Mathf.Deg2Rad);
        slashObj.transform.rotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);

        // Play slash animations on player
        PlayerAnimations.Instance.Slash();

        // Start collecting target hits
        slashObj.SetActive(true);
        StartCoroutine(RotateSlash(angle));
    }

    /// <summary>
    /// Rotate slash object for sweeping hits
    /// </summary>
    /// <param name="startAngle">Angle rotation starts at</param>
    private IEnumerator RotateSlash(float startAngle)
    {
        float angleOpening = GetHeldAdditive(_timeSlashBegan, _slashMinHoldTime, _slashMaxHoldTime, _slashBaseAngle, _slashMaxAngle) + _slashBaseAngle;

        Quaternion targetAngle = Quaternion.Euler(0, 0, (angleOpening + startAngle * Mathf.Rad2Deg));

        float speed = GetHeldAdditive(_timeSlashBegan, _slashMinHoldTime, _slashMaxHoldTime, _slashBaseSpeed, _slashMaxSpeed) + _slashBaseSpeed;

        // Rotate into position
        while (slashObj.transform.rotation != targetAngle && currentAction == SwordActions.Slashing)
        {
            slashObj.transform.rotation = Quaternion.RotateTowards(slashObj.transform.rotation, targetAngle, speed * Time.deltaTime);
            yield return null;
        }

        Invoke(nameof(EndSlash), 0.3f);

        yield break;
    }

    /// <summary>
    /// Clean up slash
    /// </summary>
    private void EndSlash()
    {
        // Stop collecting target hits
        slashObj.SetActive(false);

        PlayerController.instance.SetMovementBlock(false, "Sword");

        OnInputActionComplete.Invoke();

        // Cooldown after action before triggering another
        currentAction = SwordActions.CoolingDown;

        _cooldownStartedTime = Time.time;

        _currentCooldownDuration = _cooldownSlash;
        Invoke(nameof(CooledDown), _cooldownSlash);
    }

    /// <summary>
    /// Stab forward
    /// </summary>
    private void Stab()
    {
        currentAction = SwordActions.Stabbing;

        PlayerController.instance.SetMovementBlock(true, "Sword");

        DealDamageBox(_stabRange, _stabBreadth, _stabBaseDamage);

        // Get and set rotation of the sword
        Vector2 direction = AttackDirection();
        float angle = Mathf.Atan2(direction.y, direction.x);
        stabObj.transform.rotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);

        stabObj.SetActive(true);

        Invoke(nameof(EndStab), .2f);
    }

    /// <summary>
    /// Clean up stab
    /// </summary>
    private void EndStab()
    {
        stabObj.SetActive(false);

        PlayerController.instance.SetMovementBlock(false, "Sword");

        OnInputActionComplete.Invoke();

        // Cooldown after action before triggering another
        currentAction = SwordActions.CoolingDown;

        _cooldownStartedTime = Time.time;

        _currentCooldownDuration = _cooldownStab;
        Invoke(nameof(CooledDown), _cooldownStab);
    }

    /// <summary>
    /// Cooldown ends, cleanup
    /// </summary>
    private void CooledDown()
    {
        currentAction = SwordActions.Waiting;

        // Start building a slash
        if (_isSlashHeld)
        {
            _isSlashQueued = false;
            SlashStart();
        }
        // Slash immediately
        else if (_isSlashQueued)
        {
            _isSlashQueued = false;

            _timeSlashBegan = Time.time;
            SlashRelease();
        }
        // Stab immediately
        else if (_isStabQueued)
        {
            _isStabQueued = false;
            Stab();
        }

        _currentCooldownDuration = 0f;
    }

    /// <returns>Is cooldown close enough to end of cooldown to queue a clicked action?</returns>
    public bool CanQueueAction()
    {
        float currentTimeCoolingDown = Time.time - _cooldownStartedTime;
        float timeToAllowQueue = _currentCooldownDuration - _queueTimeFromEndOfCooldown;

        return currentTimeCoolingDown > timeToAllowQueue;
    }

    /// <returns>Direction of the attack from the wielder.</returns>
    private Vector2 AttackDirection()
    {
        Vector2 start = wielder.GetAttackOrigin();
        Vector2 direction = (wielder.GetTargetPosition() - start).normalized;

        return direction;
    }

    /// <param name="startTime">Time hold started</param>
    /// <param name="minHeldTime">Minimum time input is held to count towards held time</param>
    /// <param name="maxHeldTime">Time held to reach max multipliers</param>
    /// <param name="baseValue">Base stat value</param>
    /// <param name="maxValue">Maximum value of stat</param>
    /// <returns>Amount to add to base value</returns>
    private float GetHeldAdditive(float startTime, float minHeldTime, float maxHeldTime, float baseValue, float maxValue)
    {
        float additive = 0;

        if (Time.time - startTime > minHeldTime)
        {
            float finalHeldTime = Mathf.Clamp(Time.time - startTime, 0, maxHeldTime);
            float additivePercent = (finalHeldTime / maxHeldTime);

            additive = (additivePercent * (maxValue - baseValue));
        }

        return additive;
    }
}
