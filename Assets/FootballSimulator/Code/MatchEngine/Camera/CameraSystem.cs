using UnityEngine;
using FStudio.Utilities;
using FStudio.MatchEngine.Balls;
using FStudio.Events;
using FStudio.UI.Events;
using FStudio.MatchEngine.Events;
using System.Threading.Tasks;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FStudio.MatchEngine.Cameras {
    [RequireComponent (typeof (Camera))]
    public class CameraSystem : SceneObjectSingleton<CameraSystem> {
        public new Camera camera = default;

        [SerializeField] private SerializableAssetCollection<string, MatchCamera> matchCameras = 
            new SerializableAssetCollection<string, MatchCamera>();

        [SerializeField] public Transform target = default;

        [SerializeField] private float positionDifferencePower = 0.25f;
        [SerializeField] private float rotationDifferencePower = 0.25f;

        public float ZoomMultiplier;

        private bool isInTransition = false;
        private float transitionValue = 1;

        private bool instantTransitionInNextFrame = false;

        private int cameraDragTouchId = -1;
        private float previousPinchDistance;
        private float touchYawOffset;
        private float touchZoomMultiplier = 1f;

        [SerializeField] private float transitionSpeed = 0.5f;


        [Header("Camera Speed")]
        public float CameraPositionSpeed = 4;
        public float CameraRotationSpeed = 20;
        public float CameraZoomSpeed = 4;

        public MatchCamera CurrentCamera { get; private set; }

        public string CurrentCameraType { get; private set; }

        public Vector3? TargetPosition;

        private void ActiveCameraChanged () {
            if (target == null) {
                EventManager.Trigger<MatchCameraActiveEvent>(null);
            } else {
                EventManager.Trigger(new MatchCameraActiveEvent());
            }
        }

        public void SetTarget(Transform target) {
            Debug.Log($"[SetTarget] {target}");
            this.target = target;
            ActiveCameraChanged();
        }

        public async Task SwitchCamera(string cameraType) {
            Debug.Log($"[CameraSystem] Switch Camera: {cameraType}");
            CurrentCamera = await matchCameras.FindAsync(cameraType);
            CurrentCameraType = cameraType;
            isInTransition = false;
            instantTransitionInNextFrame = true; 

            ActiveCameraChanged();
        }

        private async void Start() {
            await SwitchCamera("Stadium"); // default camera.
        }

        /// <summary>
        /// Make the transition instant.
        /// </summary>
        public void FocusToBall () {
            SetTarget(Ball.Current.transform);
            TargetPosition = null;

            instantTransitionInNextFrame = true;
            isInTransition = false;
        }

        private void OnValidate() {
            camera = GetComponent<Camera>();
        }

        private void Update() {
            UpdateMobileCameraInput();

            if (target == null) {
                return;
            }

            var dT = Time.fixedDeltaTime;

            if (isInTransition) {
                transitionValue += dT * transitionSpeed;

                if (transitionValue >= 1 ) {
                    isInTransition = false;
                }
            } else {
                transitionValue = 1;
            }

            if (CurrentCamera != null) {
                Vector3 targetPos;
                if (TargetPosition.HasValue) {
                    targetPos = TargetPosition.Value;
                } else {
                    if (target == null) {
                        return;
                    }

                    targetPos = target.position;
                }

                var (position, rotation, zoom) = CurrentCamera.Behave(in dT, targetPos);

                if (Application.isMobilePlatform) {
                    var touchRotation = Quaternion.Euler(0f, touchYawOffset, 0f);
                    position = targetPos + touchRotation * (position - targetPos);
                    rotation = touchRotation * rotation;
                    zoom /= touchZoomMultiplier;
                }

                
                if (instantTransitionInNextFrame) {
                    instantTransitionInNextFrame = false;

                    transform.position = position;
                    transform.rotation = rotation;
                    camera.fieldOfView = zoom / (ZoomMultiplier + 1);
                } else {
                    var positionDifference = (Vector3.Distance(transform.position, position)+1) * transitionValue;
                    var rotationDifference = (1+ Quaternion.Angle(transform.rotation, rotation)) * transitionValue;
                    transform.position = Vector3.Lerp(transform.position, position, dT * CameraPositionSpeed * positionDifference * positionDifferencePower);
                    transform.rotation = Quaternion.Lerp(transform.rotation, rotation, dT * CameraRotationSpeed * rotationDifference * rotationDifferencePower);

                    camera.fieldOfView = Mathf.Lerp(camera.fieldOfView, zoom / (ZoomMultiplier + 1), dT * CameraZoomSpeed);
                }
            }
        }

        private void UpdateMobileCameraInput() {
            if (!Application.isMobilePlatform) {
                return;
            }

            var touchscreen = Touchscreen.current;
            if (touchscreen == null) {
                cameraDragTouchId = -1;
                previousPinchDistance = 0f;
                return;
            }

            TouchControl firstTouch = null;
            TouchControl secondTouch = null;
            var activeTouchCount = 0;

            foreach (var touch in touchscreen.touches) {
                if (!touch.press.isPressed) {
                    continue;
                }

                if (activeTouchCount == 0) {
                    firstTouch = touch;
                } else if (activeTouchCount == 1) {
                    secondTouch = touch;
                }

                activeTouchCount++;
            }

            if (activeTouchCount >= 2) {
                cameraDragTouchId = -1;
                var firstId = firstTouch.touchId.ReadValue();
                var secondId = secondTouch.touchId.ReadValue();
                if (!IsPointerOverUI(firstId) && !IsPointerOverUI(secondId)) {
                    var distance = Vector2.Distance(
                        firstTouch.position.ReadValue(),
                        secondTouch.position.ReadValue());

                    if (previousPinchDistance > 0f) {
                        touchZoomMultiplier = Mathf.Clamp(
                            touchZoomMultiplier * distance / previousPinchDistance,
                            0.75f,
                            1.4f);
                    }

                    previousPinchDistance = distance;
                } else {
                    previousPinchDistance = 0f;
                }

                return;
            }

            previousPinchDistance = 0f;
            if (activeTouchCount == 0) {
                cameraDragTouchId = -1;
                return;
            }

            var touchId = firstTouch.touchId.ReadValue();
            var phase = firstTouch.phase.ReadValue();
            if (phase == UnityEngine.InputSystem.TouchPhase.Began) {
                cameraDragTouchId = IsPointerOverUI(touchId) ? -1 : touchId;
            } else if (touchId == cameraDragTouchId && phase == UnityEngine.InputSystem.TouchPhase.Moved) {
                var delta = firstTouch.delta.ReadValue();
                touchYawOffset += delta.x * 0.12f;
                touchZoomMultiplier = Mathf.Clamp(
                    touchZoomMultiplier * Mathf.Exp(-delta.y * 0.0015f),
                    0.75f,
                    1.4f);
            }
        }

        private static bool IsPointerOverUI(int pointerId) {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);
        }
    }
}
