using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Fly camera: WASD moves, Q and E go down and up, Shift is faster, Escape frees the cursor</summary>
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float mouseSensitivity = 2f;

    private float yaw;
    private float pitch;
    private bool cameraMode;

    void Start()
    {
        SetCameraMode(true);

        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
    }

    /// <summary>Locks and hides the cursor while the mouse steers the camera</summary>
    void SetCameraMode(bool on)
    {
        cameraMode = on;
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !on;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetCameraMode(!cameraMode);
        else if (!cameraMode && Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
            SetCameraMode(true);

        if (!cameraMode)
            return;

        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);
        transform.eulerAngles = new Vector3(pitch, yaw, 0f);

        float up = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        Vector3 move = transform.forward * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal") + transform.up * up;

        float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed * 2f : moveSpeed;
        transform.position += move.normalized * speed * Time.deltaTime;
    }
}
