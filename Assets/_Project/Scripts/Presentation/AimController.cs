using System.Collections.Generic;
using Orbital.Physics;
using Orbital.Strategy;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbital.Presentation
{
    /// <summary>
    /// Handles mouse input for positioning, aiming, and launching the rocket.
    /// Active only when GameState.Phase == WaitingForLaunch (turn-managed) or
    /// Status == Prelaunch (unmanaged / Phase 1–2 fallback).
    ///
    /// Single-drag interaction model:
    ///   1. POSITIONING — while cursor stays inside (home.Radius + PositioningThreshold):
    ///      rocket slides around home's surface, following cursor angle.
    ///      Aim arrow and trajectory are hidden.
    ///   2. AIMING — once cursor moves outside the threshold (one-way transition):
    ///      rocket position freezes. Aim arrow and trajectory preview appear.
    ///      Releasing the mouse fires the rocket.
    ///   If mouse is released while still in positioning, the drag ends with no launch.
    ///   The rocket stays at its last surface position ready for the next drag.
    /// </summary>
    public class AimController : MonoBehaviour
    {
        [Tooltip("World-space radius around the rocket that accepts a click to start a drag.")]
        public float ClickRadius = 1.0f;

        [Tooltip("Drag distance in world units that maps to full thrust (thrust01 = 1).")]
        public float MaxDragDistance = 5f;

        [Tooltip("Distance past the home planet surface at which the drag transitions from " +
                 "positioning to aiming. Larger values give a bigger 'safe zone' to reposition.")]
        public float PositioningThreshold = 3f;

        [Tooltip("Gap between the home planet surface and the rendered rocket position " +
                 "during positioning, in world units.")]
        public float RocketSurfaceOffset = 0.7f;

        // -------------------------------------------------------------------------
        //  Wired in via Initialize / SetTurnManager / SetHomePlanet
        // -------------------------------------------------------------------------

        private PrototypeScenarioController _scenario;
        private TrajectoryView _trajectoryView;
        private TurnManager _turnManager;
        private CelestialBody _homeBody;

        // -------------------------------------------------------------------------
        //  View
        // -------------------------------------------------------------------------

        private LineRenderer _aimArrow;
        private Camera _cam;

        // -------------------------------------------------------------------------
        //  Drag state
        // -------------------------------------------------------------------------

        private bool _isDragging;
        private bool _isInAimingPhase;   // one-way flag; set when cursor leaves positioning zone
        private Vector2 _frozenRocketPos; // world position where rocket is frozen at aiming start

        // -------------------------------------------------------------------------
        //  Public API
        // -------------------------------------------------------------------------

        public void Initialize(PrototypeScenarioController scenario, TrajectoryView trajectoryView)
        {
            _scenario = scenario;
            _trajectoryView = trajectoryView;
            _cam = Camera.main;
            _aimArrow = CreateAimArrow();
        }

        /// <summary>Wire in TurnManager so AimController gates on GamePhase.</summary>
        public void SetTurnManager(TurnManager turnManager)
        {
            _turnManager = turnManager;
        }

        /// <summary>
        /// Tell AimController which body is the active player's home this turn.
        /// Called by TurnManager.StartTurn() so positioning knows which planet to orbit.
        /// </summary>
        public void SetHomePlanet(CelestialBody homeBody)
        {
            _homeBody = homeBody;
        }

        /// <summary>Update the aim arrow colour to match the active player.</summary>
        public void SetPlayerColor(Color color)
        {
            if (_aimArrow == null) return;
            _aimArrow.startColor = new Color(color.r, color.g, color.b, 0.9f);
            _aimArrow.endColor   = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f, 0.5f);
        }

        // -------------------------------------------------------------------------
        //  Unity messages
        // -------------------------------------------------------------------------

        private void Update()
        {
            if (_scenario == null) return;

            // Phase gate: only active during WaitingForLaunch when turn-managed.
            if (_turnManager != null
                && _turnManager.GameState?.Phase != GamePhase.WaitingForLaunch)
            {
                CancelDrag();
                return;
            }

            if (_scenario.Rocket.Status != RocketStatus.Prelaunch)
            {
                CancelDrag();
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 mouseWorld = _cam.ScreenToWorldPoint(mouse.position.ReadValue());

            // ---- Start drag on click near rocket ----
            if (mouse.leftButton.wasPressedThisFrame && !_isDragging)
            {
                if ((mouseWorld - _scenario.Rocket.Position).magnitude <= ClickRadius)
                {
                    _isDragging      = true;
                    _isInAimingPhase = false;
                }
            }

            // ---- During drag ----
            if (_isDragging)
            {
                // Check for phase transition: has cursor left the positioning zone?
                if (!_isInAimingPhase)
                {
                    bool outsideZone = _homeBody == null ||
                        (mouseWorld - _homeBody.Position).magnitude
                            > _homeBody.Radius + PositioningThreshold;

                    if (outsideZone)
                    {
                        // Transition to aiming — freeze rocket at its current surface position.
                        _frozenRocketPos = _scenario.Rocket.Position;
                        _isInAimingPhase = true;
                    }
                    else
                    {
                        // Positioning: slide rocket around home surface following cursor angle.
                        Vector2 dir        = (_homeBody == null)
                            ? Vector2.right
                            : (mouseWorld - _homeBody.Position).normalized;
                        Vector2 surfacePos = _homeBody.Position
                                          + dir * (_homeBody.Radius + RocketSurfaceOffset);
                        _scenario.RepositionRocket(surfacePos);

                        // Aim arrow and trajectory are hidden during positioning.
                        if (_aimArrow != null) _aimArrow.enabled = false;
                        _trajectoryView.Hide();
                    }
                }

                if (_isInAimingPhase)
                {
                    Vector2 delta    = mouseWorld - _frozenRocketPos;
                    float thrust01   = Mathf.Clamp01(delta.magnitude / MaxDragDistance);
                    Vector2 velocity = delta.normalized * (thrust01 * _scenario.MaxLaunchSpeed);

                    UpdateAimArrow(_frozenRocketPos, mouseWorld);

                    // Trajectory: clone with frozen position so prediction starts there.
                    RocketState hypo = _scenario.Rocket.Clone();
                    hypo.Position = _frozenRocketPos;
                    hypo.Velocity = velocity;
                    List<Vector2> pts = TrajectoryPredictor.Predict(
                        hypo, _scenario.Bodies, _scenario.TrajectorySteps, _scenario.Dt, _scenario.G);
                    _trajectoryView.Show(pts);
                }
            }

            // ---- Release ----
            if (mouse.leftButton.wasReleasedThisFrame && _isDragging)
            {
                if (_isInAimingPhase)
                {
                    Vector2 delta  = mouseWorld - _frozenRocketPos;
                    float thrust01 = Mathf.Clamp01(delta.magnitude / MaxDragDistance);
                    _scenario.LaunchRocket(delta.normalized * (thrust01 * _scenario.MaxLaunchSpeed));
                }
                // If still in positioning phase: no launch — rocket stays at its surface position.
                CancelDrag();
            }
        }

        // -------------------------------------------------------------------------
        //  Private helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Cancel any in-progress drag and hide aim arrow / trajectory.
        /// Called by TurnManager when switching to a different launch site mid-turn.
        /// </summary>
        public void CancelDrag()
        {
            _isDragging      = false;
            _isInAimingPhase = false;
            _trajectoryView.Hide();
            if (_aimArrow != null)
                _aimArrow.enabled = false;
        }

        private void UpdateAimArrow(Vector2 from, Vector2 to)
        {
            _aimArrow.enabled = true;
            _aimArrow.SetPosition(0, new Vector3(from.x, from.y, -0.1f));
            _aimArrow.SetPosition(1, new Vector3(to.x,   to.y,   -0.1f));
        }

        private LineRenderer CreateAimArrow()
        {
            GameObject go = new GameObject("AimArrow");
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace  = true;
            lr.positionCount  = 2;
            lr.startWidth     = 0.08f;
            lr.endWidth       = 0.04f;
            lr.numCapVertices = 4;
            lr.material       = new Material(Shader.Find("Sprites/Default"));
            lr.startColor     = new Color(1f, 1f, 0f, 0.9f);
            lr.endColor       = new Color(1f, 0.5f, 0f, 0.5f);
            lr.enabled        = false;
            return lr;
        }
    }
}
