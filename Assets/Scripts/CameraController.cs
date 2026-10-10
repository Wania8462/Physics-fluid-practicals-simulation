using UnityEngine;
using UnityEngine.EventSystems;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform cam;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float mouseSensitivity = 2f;

    private float yaw;
    private float pitch;
    private bool cameraMode;

    void Start()
    {
        SetCameraMode(true);

        Vector3 angles = cam.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
    }

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
        cam.eulerAngles = new Vector3(pitch, yaw, 0f);

        float up = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        Vector3 move = cam.forward * Input.GetAxisRaw("Vertical") + cam.right * Input.GetAxisRaw("Horizontal") + cam.up * up;

        float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed * 2f : moveSpeed;
        cam.position += move.normalized * speed * Time.deltaTime;
    }
}
