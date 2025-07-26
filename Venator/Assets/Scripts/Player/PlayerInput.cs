using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TarodevController
{
    public class PlayerInput : MonoBehaviour
    {
#if ENABLE_INPUT_SYSTEM
        private PlayerInputActions _actions;
        private InputAction _move, _jump, _roll, _dash, _sprint;

        private void Awake()
        {
            _actions = new PlayerInputActions();
            _move = _actions.Player.Move;
            _jump = _actions.Player.Jump;
            _roll = _actions.Player.Roll;
            //_dash = _actions.Player.Dash;
            _sprint = _actions.Player.Sprint;
        }

        private void OnEnable() => _actions.Enable();

        private void OnDisable() => _actions.Disable();

        public FrameInput Gather()
        {
            return new FrameInput
            {
                JumpDown = _jump.WasPressedThisFrame(),
                JumpHeld = _jump.IsPressed(),
                RollDown = _roll.WasPressedThisFrame(),
                //DashDown = _dash.WasPressedThisFrame(),
                Move = _move.ReadValue<Vector2>(),
                SprintHeld = _sprint.IsPressed()
            };
        }
#else
    public FrameInput Gather()
        {
            return new FrameInput
            {
                JumpDown = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.C),
                JumpHeld = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.C),
                RollDown = Input.GetKeyDown(KeyCode.X) || Input.GetMouseButtonDown(1),
                //DashDown = Input.GetKeyDown(KeyCode.X) || Input.GetMouseButtonDown(1),
                Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"))
            };
        }
#endif
    }

    public struct FrameInput
    {
        public Vector2 Move;
        public bool JumpDown;
        public bool JumpHeld;
        public bool RollDown;
        //public bool DashDown;
        public bool SprintHeld;
    }
}