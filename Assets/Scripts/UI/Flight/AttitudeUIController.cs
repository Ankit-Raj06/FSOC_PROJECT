using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AttitudeUIController : MonoBehaviour
{
    public ICameraTracker cameraTracker;

    public Button btnPrograde;
    public Button btnRetrograde;
    public Button btnNadir;
    public Button btnZenith;
    public Button btnNormal;
    public Button btnAntiNormal;

    public Button btnHold;

    public TextMeshProUGUI currentAttitudeLockText;

    private TextMeshProUGUI btnText;

    public Toggle tSnap;
    public Slider slewRate;

    private ThrustController thrustController;

    // UI button mapping
    private Dictionary<AttitudeController.PointingMode, Button> modeToButton =
        new Dictionary<AttitudeController.PointingMode, Button>();


    // UI state
    private bool attitudeLocked = false;

    private AttitudeController.PointingMode lastAutoMode =
        AttitudeController.PointingMode.Velocity;


    // Current body/mode tracking
    private NBody _lastBody;

    private AttitudeController.PointingMode _lastModeMirror;

    private bool _haveMirror = false;

    private bool _lastNodeBurnLock;


    // Get the current attitude controller
    AttitudeController CurrentAtt =>
        cameraTracker?.CurrentBody
            ? cameraTracker.CurrentBody.GetComponent<AttitudeController>()
            : null;


    // =========================================================
    // INITIALIZATION
    // =========================================================

    public void Initialize(SimContext ctx)
    {
        this.cameraTracker = ctx.CameraTracker;
        this.thrustController = ctx.ThrustController;

        // Make sure the dictionary is initialized
        InitializeModeButtons();
    }


    private void InitializeModeButtons()
    {
        modeToButton = new Dictionary<AttitudeController.PointingMode, Button>
        {
            {
                AttitudeController.PointingMode.Velocity,
                btnPrograde
            },

            {
                AttitudeController.PointingMode.Retrograde,
                btnRetrograde
            },

            {
                AttitudeController.PointingMode.Nadir,
                btnNadir
            },

            {
                AttitudeController.PointingMode.Zenith,
                btnZenith
            },

            {
                AttitudeController.PointingMode.Normal,
                btnNormal
            },

            {
                AttitudeController.PointingMode.AntiNormal,
                btnAntiNormal
            }
        };
    }


    // =========================================================
    // UNITY LIFECYCLE
    // =========================================================

    void Awake()
    {
        InitializeModeButtons();
    }


    void Start()
    {
        if (btnHold)
        {
            btnText = btnHold.GetComponentInChildren<TextMeshProUGUI>();
        }

        // Start in auto mode
        SetHoldUI(false);

        ForceFullRefresh();
    }


    void OnEnable()
    {
        // Make sure dictionary exists
        if (modeToButton == null || modeToButton.Count == 0)
        {
            InitializeModeButtons();
        }


        // -------------------------
        // Attitude buttons
        // -------------------------

        if (btnPrograde)
        {
            btnPrograde.onClick.AddListener(
                () => SetMode(AttitudeController.PointingMode.Velocity)
            );
        }

        if (btnRetrograde)
        {
            btnRetrograde.onClick.AddListener(
                () => SetMode(AttitudeController.PointingMode.Retrograde)
            );
        }

        if (btnNadir)
        {
            btnNadir.onClick.AddListener(
                () => SetMode(AttitudeController.PointingMode.Nadir)
            );
        }

        if (btnZenith)
        {
            btnZenith.onClick.AddListener(
                () => SetMode(AttitudeController.PointingMode.Zenith)
            );
        }

        if (btnNormal)
        {
            btnNormal.onClick.AddListener(
                () => SetMode(AttitudeController.PointingMode.Normal)
            );
        }

        if (btnAntiNormal)
        {
            btnAntiNormal.onClick.AddListener(
                () => SetMode(AttitudeController.PointingMode.AntiNormal)
            );
        }


        // -------------------------
        // Hold button
        // -------------------------

        if (btnHold)
        {
            btnHold.onClick.AddListener(HoldHere);
        }


        // -------------------------
        // Snap toggle
        // -------------------------

        if (tSnap)
        {
            tSnap.onValueChanged.AddListener(SetSnap);
        }


        // -------------------------
        // Slew rate slider
        // -------------------------

        if (slewRate)
        {
            slewRate.onValueChanged.AddListener(SetSlew);
        }


        ForceFullRefresh();
    }


    void OnDisable()
    {
        if (btnPrograde)
            btnPrograde.onClick.RemoveAllListeners();

        if (btnRetrograde)
            btnRetrograde.onClick.RemoveAllListeners();

        if (btnNadir)
            btnNadir.onClick.RemoveAllListeners();

        if (btnZenith)
            btnZenith.onClick.RemoveAllListeners();

        if (btnNormal)
            btnNormal.onClick.RemoveAllListeners();

        if (btnAntiNormal)
            btnAntiNormal.onClick.RemoveAllListeners();

        if (btnHold)
            btnHold.onClick.RemoveAllListeners();

        if (tSnap)
            tSnap.onValueChanged.RemoveAllListeners();

        if (slewRate)
            slewRate.onValueChanged.RemoveAllListeners();
    }


    // =========================================================
    // UPDATE
    // =========================================================

    void Update()
    {
        var currentBody = cameraTracker?.CurrentBody;

        // Detect satellite/body change
        if (currentBody != _lastBody)
        {
            _lastBody = currentBody;

            ForceFullRefresh();

            return;
        }


        var att = CurrentAtt;

        if (!att)
            return;


        // -------------------------
        // Node burn lock
        // -------------------------

        bool nodeBurnLocked = IsLockedByNodeBurn();

        if (nodeBurnLocked != _lastNodeBurnLock)
        {
            _lastNodeBurnLock = nodeBurnLocked;

            UpdateModeButtons(att.mode);

            RefreshUIFrom(att);
        }


        // -------------------------
        // Detect attitude mode change
        // -------------------------

        if (!_haveMirror || att.mode != _lastModeMirror)
        {
            // Mode changed somewhere else.
            // Reflect it in the UI.

            _lastModeMirror = att.mode;

            _haveMirror = true;

            RefreshUIFrom(att);

            UpdateModeButtons(att.mode);
        }


        RefreshAuxiliaryControls();
    }


    // =========================================================
    // ATTITUDE MODE
    // =========================================================

    void SetMode(AttitudeController.PointingMode m)
    {
        if (IsLockedByNodeBurn())
            return;


        var att = CurrentAtt;

        if (!att)
            return;


        // If holding and user selects an auto mode,
        // exit hold.

        if (att.mode == AttitudeController.PointingMode.HoldCurrent &&
            m != AttitudeController.PointingMode.HoldCurrent)
        {
            SetHoldUI(false);
        }


        if (m != AttitudeController.PointingMode.HoldCurrent)
        {
            lastAutoMode = m;
        }


        att.SetMode(m);


        // Mirror state immediately
        // so Update() does not overwrite it.

        _lastModeMirror = att.mode;

        _haveMirror = true;


        RefreshUIFrom(att);

        UpdateModeButtons(att.mode);
    }


    // =========================================================
    // HOLD ATTITUDE
    // =========================================================

    void HoldHere()
    {
        if (IsLockedByNodeBurn())
            return;


        var att = CurrentAtt;

        if (!att)
            return;


        bool goingToHold =
            att.mode != AttitudeController.PointingMode.HoldCurrent;


        if (goingToHold)
        {
            // Entering hold.
            // Remember current auto mode.

            if (att.mode != AttitudeController.PointingMode.HoldCurrent)
            {
                lastAutoMode = att.mode;
            }


            att.FreezeCurrentAttitude();

            SetHoldUI(true);
        }
        else
        {
            // Leaving hold.
            // Restore previous auto mode.

            att.SetMode(lastAutoMode);

            SetHoldUI(false);
        }


        // Keep mirror in sync

        _lastModeMirror = att.mode;

        _haveMirror = true;


        RefreshUIFrom(att);

        UpdateModeButtons(att.mode);
    }


    // =========================================================
    // HOLD UI
    // =========================================================

    void SetHoldUI(bool isLocked)
    {
        attitudeLocked = isLocked;


        if (btnText)
        {
            btnText.text =
                isLocked
                    ? "Auto Track"
                    : "Lock Attitude";
        }


        if (currentAttitudeLockText)
        {
            currentAttitudeLockText.text =
                isLocked
                    ? "Attitude is Locked"
                    : "Attitude is Auto Tracking";
        }
    }


    // =========================================================
    // UPDATE MODE BUTTONS
    // =========================================================

    void UpdateModeButtons(
        AttitudeController.PointingMode active)
    {
        // Safety check
        if (modeToButton == null || modeToButton.Count == 0)
        {
            InitializeModeButtons();
        }


        bool lockedByNodeBurn =
            IsLockedByNodeBurn();


        // -------------------------
        // Reset all buttons
        // -------------------------

        foreach (var kv in modeToButton)
        {
            var b = kv.Value;

            if (b)
            {
                b.interactable = !lockedByNodeBurn;
            }
        }


        bool holding =
            active ==
            AttitudeController.PointingMode.HoldCurrent;


        // -------------------------
        // Disable active mode
        // -------------------------

        if (!lockedByNodeBurn)
        {
            foreach (var kv in modeToButton)
            {
                var b = kv.Value;

                if (!b)
                    continue;


                if (holding)
                {
                    // In Hold mode,
                    // none are selected.

                    b.interactable = true;
                }
                else
                {
                    // Disable the currently active mode.

                    b.interactable =
                        kv.Key != active;
                }
            }
        }


        // -------------------------
        // Hold button
        // -------------------------

        if (btnHold)
        {
            btnHold.interactable =
                !lockedByNodeBurn;
        }


        RefreshAuxiliaryControls();


        // -------------------------
        // Clear Unity UI selection
        // -------------------------

        if (EventSystem.current &&
            EventSystem.current.currentSelectedGameObject)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }


    // =========================================================
    // FULL UI REFRESH
    // =========================================================

    void RefreshUIFromCurrent()
    {
        var att = CurrentAtt;

        if (!att)
            return;


        bool lockedNow =
            att.mode ==
            AttitudeController.PointingMode.HoldCurrent;


        SetHoldUI(lockedNow);


        if (!lockedNow)
        {
            lastAutoMode = att.mode;
        }


        _lastModeMirror = att.mode;

        _haveMirror = true;


        RefreshUIFrom(att);

        UpdateModeButtons(att.mode);
    }


    // =========================================================
    // REFRESH UI FROM ATTITUDE CONTROLLER
    // =========================================================

    void RefreshUIFrom(AttitudeController att)
    {
        if (tSnap)
        {
            tSnap.SetIsOnWithoutNotify(
                att.snapAttitude
            );
        }


        if (slewRate)
        {
            slewRate.SetValueWithoutNotify(
                att.maxSlewRateDegPerSec
            );
        }
    }


    // =========================================================
    // SNAP
    // =========================================================

    void SetSnap(bool snap)
    {
        if (IsLockedByNodeBurn())
            return;


        var att = CurrentAtt;

        if (att)
        {
            att.snapAttitude = snap;
        }
    }


    // =========================================================
    // SLEW RATE
    // =========================================================

    void SetSlew(float degs)
    {
        if (IsLockedByNodeBurn())
            return;


        var att = CurrentAtt;

        if (att)
        {
            att.maxSlewRateDegPerSec = degs;
        }
    }


    // =========================================================
    // FORCE FULL REFRESH
    // =========================================================

    void ForceFullRefresh()
    {
        // Make absolutely sure the dictionary exists.

        if (modeToButton == null ||
            modeToButton.Count == 0)
        {
            InitializeModeButtons();
        }


        // Clear old selection visuals.

        foreach (var kv in modeToButton)
        {
            if (kv.Value)
            {
                kv.Value.interactable = true;
            }
        }


        // Clear Unity's selected UI object.

        if (EventSystem.current)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }


        _lastNodeBurnLock =
            IsLockedByNodeBurn();


        RefreshUIFromCurrent();
    }


    // =========================================================
    // NODE BURN LOCK
    // =========================================================

    private bool IsLockedByNodeBurn()
    {
        return thrustController != null &&
               thrustController.IsNodeBurnActive;
    }


    // =========================================================
    // AUXILIARY CONTROLS
    // =========================================================

    private void RefreshAuxiliaryControls()
    {
        bool interactable =
            !IsLockedByNodeBurn();


        if (tSnap)
        {
            tSnap.interactable = interactable;
        }


        if (slewRate)
        {
            slewRate.interactable = interactable;
        }
    }
}