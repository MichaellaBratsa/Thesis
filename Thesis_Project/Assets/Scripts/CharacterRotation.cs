using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class CharacterRotation : MonoBehaviour
{
    public Transform characterRoot;
    public float degreesPerScreenWidth = 180f;
    public float smooth = 15f;
    public bool invert = false;

    private float targetYaw;
    private Vector2 lastPos;
    private bool dragging;

    void OnEnable() => EnhancedTouchSupport.Enable();
    void OnDisable() => EnhancedTouchSupport.Disable();

    void Start()
    {
        if (!characterRoot) characterRoot = transform;
        targetYaw = characterRoot.eulerAngles.y;
    }

    void Update()
    {
        var touches = Touch.activeTouches;
        if (touches.Count == 0) { dragging = false; return; }

        var t = touches[0];

        if (t.phase == UnityEngine.InputSystem.TouchPhase.Began)
        {
            dragging = true;
            lastPos = t.screenPosition;
        }
        else if (t.phase == UnityEngine.InputSystem.TouchPhase.Moved && dragging)
        {
            float dx = t.screenPosition.x - lastPos.x;
            lastPos = t.screenPosition;

            float sign = invert ? -1f : 1f;
            targetYaw += (dx / Screen.width) * degreesPerScreenWidth * sign;
        }
        else if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                 t.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
        {
            dragging = false;
        }
    }

    void LateUpdate()
    {
        float currentYaw = characterRoot.eulerAngles.y;
        float newYaw = Mathf.LerpAngle(currentYaw, targetYaw, Time.deltaTime * smooth);
        characterRoot.rotation = Quaternion.Euler(0f, newYaw, 0f);
    }
}
