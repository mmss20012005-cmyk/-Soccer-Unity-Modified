using System.Collections.Generic;
using FStudio.MatchEngine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace FStudio.Input {
    internal class InputMobile : MonoBehaviour {
        private const string MATCH_ENGINE_ACTION_MAP = "MatchEngine";
        private const string CONTROL_SIZE_KEY = "MOBILE_CONTROL_SIZE";
        private const string CONTROL_OPACITY_KEY = "MOBILE_CONTROL_OPACITY";
        private const string CONTROL_MIRROR_KEY = "MOBILE_CONTROL_MIRROR";
        private const float DEFAULT_CONTROL_SIZE = 1f;
        private const float DEFAULT_CONTROL_OPACITY = 0.82f;

        private static InputMobile instance;

        [SerializeField] private GameObject cancelButton;
        [SerializeField] private GameObject canvas;

        private readonly List<ControlSnapshot> controlSnapshots = new List<ControlSnapshot>();
        private GameObject settingsPanel;
        private Text sizeLabel;
        private Text opacityLabel;
        private Text mirrorLabel;
        private float controlSize = DEFAULT_CONTROL_SIZE;
        private float controlOpacity = DEFAULT_CONTROL_OPACITY;
        private bool mirrorControls;

        private sealed class ControlSnapshot {
            public RectTransform RectTransform;
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public Vector2 Pivot;
            public Vector2 AnchoredPosition;
            public Vector3 LocalScale;
            public CanvasGroup CanvasGroup;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        internal static void Load() {
            // OnScreenControl creates and pairs a virtual Gamepad. Do not create it
            // in desktop builds or it can steal the keyboard's PlayerInput scheme.
            if (!Application.isMobilePlatform || FindObjectOfType<InputMobile>() != null) {
                return;
            }

            var prefab = Resources.Load<Transform>("InputMobile");
            if (prefab == null) {
                Debug.LogError("[InputMobile] Resources/InputMobile prefab was not found.");
                return;
            }

            var inputMobile = Instantiate(prefab);
            DontDestroyOnLoad(inputMobile.gameObject);
        }

        private void Awake() {
            if (instance != null && instance != this) {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void Start() {
            if (!Application.isMobilePlatform || canvas == null) {
                return;
            }

            LoadControlSettings();
            CaptureTouchControls();
            CreateTouchSettingsMenu();
            ApplyControlSettings();
        }

        private void OnDestroy() {
            if (instance == this) {
                instance = null;
            }
        }

        private void Update() {
            var matchEngineActive = MatchManager.Current != null;
            var hasUserTeam = MatchManager.Current?.UserTeam != null;
            var playerInput = PlayerInput.all.Count > 0 ? PlayerInput.all[0] : null;
            var matchMapActive = playerInput != null &&
                playerInput.currentActionMap != null &&
                playerInput.currentActionMap.name == MATCH_ENGINE_ACTION_MAP;

            var controlsVisible = Application.isMobilePlatform && matchMapActive &&
                matchEngineActive && hasUserTeam;

            if (canvas != null && canvas.activeSelf != controlsVisible) {
                canvas.SetActive(controlsVisible);
            }

            var cancelVisible = Application.isMobilePlatform && matchMapActive && matchEngineActive;
            if (cancelButton != null && cancelButton.activeSelf != cancelVisible) {
                cancelButton.SetActive(cancelVisible);
            }
        }

        private void LoadControlSettings() {
            controlSize = Mathf.Clamp(PlayerPrefs.GetFloat(CONTROL_SIZE_KEY, DEFAULT_CONTROL_SIZE), 0.75f, 1.35f);
            controlOpacity = Mathf.Clamp(PlayerPrefs.GetFloat(CONTROL_OPACITY_KEY, DEFAULT_CONTROL_OPACITY), 0.35f, 1f);
            mirrorControls = PlayerPrefs.GetInt(CONTROL_MIRROR_KEY, 0) == 1;
        }

        private void CaptureTouchControls() {
            controlSnapshots.Clear();
            var controls = canvas.GetComponentsInChildren<OnScreenControl>(true);
            foreach (var control in controls) {
                var controlTransform = control is OnScreenStick && control.transform.parent != null
                    ? control.transform.parent
                    : control.transform;
                var rect = controlTransform as RectTransform;
                if (rect == null) {
                    continue;
                }

                var canvasGroup = controlTransform.GetComponent<CanvasGroup>();
                if (canvasGroup == null) {
                    canvasGroup = controlTransform.gameObject.AddComponent<CanvasGroup>();
                }

                controlSnapshots.Add(new ControlSnapshot {
                    RectTransform = rect,
                    AnchorMin = rect.anchorMin,
                    AnchorMax = rect.anchorMax,
                    Pivot = rect.pivot,
                    AnchoredPosition = rect.anchoredPosition,
                    LocalScale = rect.localScale,
                    CanvasGroup = canvasGroup
                });
            }
        }

        private void ApplyControlSettings() {
            foreach (var control in controlSnapshots) {
                if (control.RectTransform == null) {
                    continue;
                }

                var rect = control.RectTransform;
                if (mirrorControls) {
                    rect.anchorMin = new Vector2(1f - control.AnchorMax.x, control.AnchorMin.y);
                    rect.anchorMax = new Vector2(1f - control.AnchorMin.x, control.AnchorMax.y);
                    rect.pivot = new Vector2(1f - control.Pivot.x, control.Pivot.y);
                    rect.anchoredPosition = new Vector2(-control.AnchoredPosition.x, control.AnchoredPosition.y);
                } else {
                    rect.anchorMin = control.AnchorMin;
                    rect.anchorMax = control.AnchorMax;
                    rect.pivot = control.Pivot;
                    rect.anchoredPosition = control.AnchoredPosition;
                }

                rect.localScale = control.LocalScale * controlSize;
                control.CanvasGroup.alpha = controlOpacity;
            }

            UpdateSettingsLabels();
        }

        private void CreateTouchSettingsMenu() {
            var settingsButton = CreateButton(
                canvas.transform,
                "Touch Controls Settings Button",
                "SET",
                new Vector2(1f, 1f),
                new Vector2(-64f, -54f),
                new Vector2(92f, 72f));
            settingsButton.onClick.AddListener(ToggleSettingsMenu);

            var panelObject = new GameObject("Touch Controls Settings Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(canvas.transform, false);
            settingsPanel = panelObject;
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(560f, 500f);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = new Color(0.035f, 0.055f, 0.085f, 0.96f);
            panelImage.raycastTarget = true;

            CreateLabel(panelObject.transform, "Touch Controls", new Vector2(0f, 205f), new Vector2(500f, 52f), 30);

            sizeLabel = CreateLabel(panelObject.transform, string.Empty, new Vector2(0f, 112f), new Vector2(260f, 50f), 23);
            CreateButton(panelObject.transform, "Decrease Control Size", "-", new Vector2(0.5f, 0.5f), new Vector2(-190f, 112f), new Vector2(82f, 58f))
                .onClick.AddListener(() => AdjustControlSize(-0.1f));
            CreateButton(panelObject.transform, "Increase Control Size", "+", new Vector2(0.5f, 0.5f), new Vector2(190f, 112f), new Vector2(82f, 58f))
                .onClick.AddListener(() => AdjustControlSize(0.1f));

            opacityLabel = CreateLabel(panelObject.transform, string.Empty, new Vector2(0f, 28f), new Vector2(260f, 50f), 23);
            CreateButton(panelObject.transform, "Decrease Control Opacity", "-", new Vector2(0.5f, 0.5f), new Vector2(-190f, 28f), new Vector2(82f, 58f))
                .onClick.AddListener(() => AdjustControlOpacity(-0.1f));
            CreateButton(panelObject.transform, "Increase Control Opacity", "+", new Vector2(0.5f, 0.5f), new Vector2(190f, 28f), new Vector2(82f, 58f))
                .onClick.AddListener(() => AdjustControlOpacity(0.1f));

            mirrorLabel = CreateLabel(panelObject.transform, string.Empty, new Vector2(0f, -60f), new Vector2(390f, 52f), 21);
            CreateButton(panelObject.transform, "Mirror Controls", "SWAP SIDES", new Vector2(0.5f, 0.5f), new Vector2(0f, -118f), new Vector2(300f, 58f))
                .onClick.AddListener(ToggleMirrorControls);
            CreateButton(panelObject.transform, "Reset Touch Controls", "RESET", new Vector2(0.5f, 0.5f), new Vector2(-95f, -205f), new Vector2(170f, 58f))
                .onClick.AddListener(ResetControlSettings);
            CreateButton(panelObject.transform, "Close Touch Controls Settings", "DONE", new Vector2(0.5f, 0.5f), new Vector2(95f, -205f), new Vector2(170f, 58f))
                .onClick.AddListener(ToggleSettingsMenu);

            settingsPanel.SetActive(false);
            UpdateSettingsLabels();
        }

        private Button CreateButton(Transform parent, string objectName, string label, Vector2 anchor, Vector2 position, Vector2 size) {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.25f, 0.36f, 0.98f);
            image.raycastTarget = true;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            CreateLabel(buttonObject.transform, label, Vector2.zero, size - new Vector2(8f, 8f), 22);
            return button;
        }

        private Text CreateLabel(Transform parent, string value, Vector2 position, Vector2 size, int fontSize) {
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(parent, false);

            var rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = labelObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private void ToggleSettingsMenu() {
            if (settingsPanel != null) {
                settingsPanel.SetActive(!settingsPanel.activeSelf);
            }
        }

        private void AdjustControlSize(float amount) {
            controlSize = Mathf.Clamp(controlSize + amount, 0.75f, 1.35f);
            SaveControlSettings();
            ApplyControlSettings();
        }

        private void AdjustControlOpacity(float amount) {
            controlOpacity = Mathf.Clamp(controlOpacity + amount, 0.35f, 1f);
            SaveControlSettings();
            ApplyControlSettings();
        }

        private void ToggleMirrorControls() {
            mirrorControls = !mirrorControls;
            SaveControlSettings();
            ApplyControlSettings();
        }

        private void ResetControlSettings() {
            controlSize = DEFAULT_CONTROL_SIZE;
            controlOpacity = DEFAULT_CONTROL_OPACITY;
            mirrorControls = false;
            SaveControlSettings();
            ApplyControlSettings();
        }

        private void SaveControlSettings() {
            PlayerPrefs.SetFloat(CONTROL_SIZE_KEY, controlSize);
            PlayerPrefs.SetFloat(CONTROL_OPACITY_KEY, controlOpacity);
            PlayerPrefs.SetInt(CONTROL_MIRROR_KEY, mirrorControls ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void UpdateSettingsLabels() {
            if (sizeLabel != null) {
                sizeLabel.text = $"Size: {Mathf.RoundToInt(controlSize * 100f)}%";
            }
            if (opacityLabel != null) {
                opacityLabel.text = $"Opacity: {Mathf.RoundToInt(controlOpacity * 100f)}%";
            }
            if (mirrorLabel != null) {
                mirrorLabel.text = mirrorControls ? "Layout: Mirrored" : "Layout: Standard";
            }
        }
    }
}
