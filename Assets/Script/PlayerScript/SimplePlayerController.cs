using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 歩き・ジャンプ・しゃがみだけのシンプルなプレイヤー。
/// Rigidbody は使わず CharacterController で動かします。
///
/// 階層:
///   Player      ← このスクリプト + CharacterController
///   └ Main Camera
///       └ 銃
///
/// 操作: WASD=移動 / Space=ジャンプ / Ctrl or C=しゃがみ / マウス=視点 / Esc=カーソル解放
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SimplePlayerController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("子にあるカメラ。未設定なら子から自動で探します")]
    public Transform cam;

    [Header("移動")]
    public float walkSpeed = 4.0f;
    public float crouchSpeed = 1.8f;

    [Header("ジャンプ")]
    [Tooltip("跳べる高さ[m]")]
    public float jumpHeight = 1.1f;
    public float gravity = -20f;

    [Header("しゃがみ")]
    public float standHeight = 1.8f;
    public float crouchHeight = 1.0f;
    [Tooltip("立ち時の目線の高さ[m]")]
    public float standEyeHeight = 1.62f;
    [Tooltip("しゃがみ時の目線の高さ[m]")]
    public float crouchEyeHeight = 0.95f;
    [Tooltip("しゃがみ／立ちの切り替わる速さ")]
    public float crouchSpeedLerp = 10f;

    [Header("視点")]
    public float mouseSensitivity = 0.12f;
    [Tooltip("ズーム中は感度を下げる（狙いやすくなります）")]
    public bool scaleSensitivityWithZoom = true;

    public bool IsCrouching { get; private set; }
    public bool IsGrounded => _cc.isGrounded;

    CharacterController _cc;
    Camera _camComp;
    float _yaw, _pitch;
    float _velY;
    float _eye;
    float _baseFov;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (!cam) cam = GetComponentInChildren<Camera>(true)?.transform;
        if (cam) _camComp = cam.GetComponent<Camera>();

        _cc.height = standHeight;
        _cc.center = new Vector3(0f, standHeight * 0.5f, 0f);
        _eye = standEyeHeight;

        _yaw = transform.eulerAngles.y;
        if (cam) _pitch = 0f;
        if (_camComp) _baseFov = _camComp.fieldOfView;
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        Look(dt);
        Crouch(dt);
        Move(dt);
        CursorToggle();
    }

    // ---------------------------------------------------------------
    void Look(float dt)
    {
        if (cam == null) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Vector2 d = LookDelta();
        float sens = mouseSensitivity;

        // ズーム中は同じマウス移動量でも振れ角が小さくなるようにする
        if (scaleSensitivityWithZoom && _camComp && _baseFov > 1f)
            sens *= _camComp.fieldOfView / _baseFov;

        _yaw += d.x * sens;
        _pitch = Mathf.Clamp(_pitch - d.y * sens, -85f, 85f);

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        cam.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    // ---------------------------------------------------------------
    void Crouch(float dt)
    {
        bool want = CrouchHeld();

        // 立ち上がれるか（頭上に障害物がないか）を確認する
        if (IsCrouching && !want && !HasHeadroom()) want = true;

        IsCrouching = want;

        float targetH = want ? crouchHeight : standHeight;
        float targetEye = want ? crouchEyeHeight : standEyeHeight;

        _cc.height = Mathf.Lerp(_cc.height, targetH, dt * crouchSpeedLerp);
        _cc.center = new Vector3(0f, _cc.height * 0.5f, 0f);

        _eye = Mathf.Lerp(_eye, targetEye, dt * crouchSpeedLerp);
        if (cam) cam.localPosition = new Vector3(0f, _eye, 0f);
    }

    bool HasHeadroom()
    {
        // 自分のコライダーに当たらないよう、一時的に無効化して判定する
        _cc.enabled = false;
        Vector3 start = transform.position + Vector3.up * (crouchHeight - _cc.radius);
        float dist = standHeight - crouchHeight + 0.05f;
        bool blocked = Physics.SphereCast(start, _cc.radius * 0.95f, Vector3.up,
                                          out _, dist, ~0, QueryTriggerInteraction.Ignore);
        _cc.enabled = true;
        return !blocked;
    }

    // ---------------------------------------------------------------
    void Move(float dt)
    {
        Vector2 input = MoveInput();
        Vector3 dir = transform.right * input.x + transform.forward * input.y;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        if (_cc.isGrounded)
        {
            if (_velY < 0f) _velY = -2f;                 // 地面に貼り付ける
            if (JumpPressed() && !IsCrouching)
                _velY = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        _velY += gravity * dt;

        float speed = IsCrouching ? crouchSpeed : walkSpeed;
        _cc.Move((dir * speed + Vector3.up * _velY) * dt);
    }

    // ---------------------------------------------------------------
    void CursorToggle()
    {
        if (EscapePressed())
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (ClickPressed() && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ---------------------------------------------------------------
    // 入力（Input System / 旧 Input Manager の両対応）
#if ENABLE_INPUT_SYSTEM
    static Vector2 MoveInput()
    {
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
        float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
    }
    static Vector2 LookDelta() =>
        Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
    static bool JumpPressed() =>
        Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
    static bool CrouchHeld() =>
        Keyboard.current != null &&
        (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed);
    static bool EscapePressed() =>
        Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    static bool ClickPressed() =>
        Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
    static Vector2 MoveInput() =>
        new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    static Vector2 LookDelta() =>
        new Vector2(Input.GetAxisRaw("Mouse X") * 10f, Input.GetAxisRaw("Mouse Y") * 10f);
    static bool JumpPressed() => Input.GetKeyDown(KeyCode.Space);
    static bool CrouchHeld() =>
        Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
    static bool EscapePressed() => Input.GetKeyDown(KeyCode.Escape);
    static bool ClickPressed() => Input.GetMouseButtonDown(0);
#endif
}
