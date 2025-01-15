using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Loadout : MonoBehaviour
{
    private LoadoutSettings _settings;

    #region Loadout Slots
    /// <summary>
    /// On a new loadout slot becoming active and equipped
    /// </summary>
    [HideInInspector]
    public UnityEvent<WeaponAttributes> OnActiveWeaponChange;

    // Weapons on this character
    private Weapon[] _slots;
    // Current active index in loadout
    private int _activeSlotIndex = 0;

    /// <summary>
    /// Actively equipped weapon
    /// </summary>
    public Weapon ActiveWeapon
    {
        get
        {
            return _slots[_activeSlotIndex];
        }
    }

    // Can this entity switch to a new weapon
    private bool _canSwitchWeapon = true;
    // Time buffer between weapon swaps
    private float _switchWeaponCooldownTime = 0.2f;
    #endregion 

    #region Combat Filtering
    // Tags to track reason added and ensure removal does not turn off combat block if blocked for multiple reasons
    private List<string> _weaponBlockTags = new List<string>();
    
    private bool _isCombatBlocked;
    /// <summary>
    /// Is combat enabled for this entity? Can weapons be used?
    /// </summary>
    public bool IsCombatBlocked
    {
        get
        {
            return _isCombatBlocked;
        }
    }

    /// <summary>
    /// Set combat blocked filter
    /// </summary>
    /// <param name="isCombatBlocked">Block combat?</param>
    /// <param name="tag">Tag associated with block</param>
    /// <returns>Is combat blocked?</returns>
    public bool SetCombatBlock(bool isCombatBlocked, string tag)
    {
        // Remove combat block tag
        if (!isCombatBlocked)
        {
            // Tag was not found, may already be removed
            if (!_weaponBlockTags.Contains(tag))
            {
                return _isCombatBlocked;
            }

            _weaponBlockTags.Remove(tag);

            if (_weaponBlockTags.Count == 0)
            {
                _isCombatBlocked = false;
            }

            return _isCombatBlocked;
        }
        
        if (isCombatBlocked)
        {
            // Tag already found
            if(_weaponBlockTags.Contains(tag))
            {
                // Tag overlaps could cause unexpected problems later when removing
                Debug.LogError("Tried to add another stack of weapon block with a tag already in use.");

                return _isCombatBlocked;
            }

            _weaponBlockTags.Add(tag);
            _isCombatBlocked = true;
        }

        return _isCombatBlocked;
    }
    #endregion

    private void Start()
    {
        SetActiveWeapon(0);
    }

    /// <summary>
    /// Initialize loadout component instance with settings
    /// </summary>
    /// <param name="settings">Loadout settings for this entity</param>
    public void InitializeLoadoutInstance(LoadoutSettings settings)
    {
        _settings = settings;
        _slots = new Weapon[_settings.slots.Length];

        // Create weaponry added to this entity
        for (int i = 0; i < _settings.slots.Length; i++)
        {
            if (!_settings.slots[i]) return;

            _slots[i] = Instantiate(_settings.slots[i].prefab, gameObject.transform).GetComponent<Weapon>();
            _slots[i].Initialize(GetComponent<Sentient>(), _settings.slots[i]);
        }
    }

    /// <summary>
    /// Replace specific slot with a new weapon 
    /// </summary>
    /// <param name="slot">Slot index to replace</param>
    /// <param name="weaponAttributes">Weapon to create and add</param>
    public void ReplaceWeaponSlot(int slot, WeaponAttributes weaponAttributes)
    {
        if (slot >= _slots.Length)
        {
            Debug.LogError("Tried to replace out of bounds slot index");
            return;
        }

        // Destroy old slot
        Destroy(_slots[slot].gameObject);

        // Initialize new weapon
        _slots[slot] = Instantiate(weaponAttributes.prefab, gameObject.transform).GetComponent<Weapon>();
        _slots[slot].Initialize(GetComponent<Sentient>(), weaponAttributes);

        Debug.Log(_slots[slot].name);
    }

    /// <summary>
    /// Set the next weapon in loadout as active
    /// </summary>
    public void SetNextActiveWeapon()
    {
        if (!_canSwitchWeapon)
        {
            return;
        }

        _activeSlotIndex = (_activeSlotIndex + 1) % (_slots.Length);

        SetActiveWeapon(_activeSlotIndex);
    }

    /// <summary>
    /// Set the last weapon in the loadout as active
    /// </summary>
    public void SetLastActiveWeapon()
    {
        if (!_canSwitchWeapon)
        {
            return;
        }

        _activeSlotIndex -= 1;
        if (_activeSlotIndex < 0 )
        {
            _activeSlotIndex = _slots.Length - 1;
        }

        SetActiveWeapon(_activeSlotIndex);
    }

    /// <summary>
    /// Set new active weapon
    /// </summary>
    /// <param name="slot">Slot to set active</param>
    private void SetActiveWeapon(int slot)
    {
        if (!_canSwitchWeapon)
        {
            return;
        }

        if (slot >= _slots.Length)
        {
            return;
        }

        _activeSlotIndex = slot;

        OnActiveWeaponChange.Invoke(_slots[_activeSlotIndex].Attributes);

        _canSwitchWeapon = false;
        Invoke("AllowWeaponSwitching", _switchWeaponCooldownTime);
    }

    /// <summary>
    /// Allow entity to switch weapons
    /// </summary>
    public void AllowWeaponSwitching()
    {
        _canSwitchWeapon = true;
    }

    /// <summary>
    /// Fire primary slot action
    /// </summary>
    public void Fire()
    {
        if (IsCombatBlocked)
        {
            return;
        }

        _slots[_activeSlotIndex].Fire();
    }

    /// <summary>
    /// Fire secondary slot action
    /// </summary>
    public void FireSecondary()
    {
        if (IsCombatBlocked)
        {
            return;
        }

        _slots[_activeSlotIndex].FireSecondary();
    }

    /// <summary>
    /// Release fire on primary slot action
    /// </summary>
    public void ReleaseFire()
    {
        if (IsCombatBlocked)
        {
            return;
        }

        _slots[_activeSlotIndex].ReleaseFire();
    }

    /// <summary>
    /// Released fire on secondary slot action
    /// </summary>
    public void ReleaseSecondaryFire()
    {
        if (IsCombatBlocked)
        {
            return;
        }

        _slots[_activeSlotIndex].ReleaseSecondaryFire();
    }
}
