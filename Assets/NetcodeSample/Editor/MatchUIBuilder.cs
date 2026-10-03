using HannibalUI.Runtime.Animation;
using HannibalUI.Runtime.Base;
using NetcodeSample.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NetcodeSample.Editor
{
    /// <summary>
    /// Builds the match UI in the open scene: a HannibalUI director with the main menu, connecting and HUD screens,
    /// a popup layer with the round-end popup prefab, the F1 debug overlay, and an Input System event system.
    /// Built in code so the layout is reproducible; rebuild by deleting the "Match UI" object and running the
    /// network setup again.
    /// </summary>
    public static class MatchUIBuilder
    {
        private const string UIFolder = "Assets/NetcodeSample/UI";
        private const string PopupPrefabPath = UIFolder + "/RoundEndPopup.prefab";
        private static readonly Vector2 s_referenceResolution = new(1920, 1080);

        private static Font s_font;
        private static DefaultControls.Resources s_resources;

        /// <summary>Returns the scene's match UI, building it first if there's none.</summary>
        public static MatchUI FindOrBuild()
        {
            MatchUI existing = Object.FindAnyObjectByType<MatchUI>(FindObjectsInactive.Include);
            EnsureEventSystem();
            if (existing != null)
            {
                return existing;
            }

            s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            s_resources = new DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            };

            GameObject root = new("Match UI");
            Undo.RegisterCreatedObjectUndo(root, "Build Match UI");
            MatchUI ui = root.AddComponent<MatchUI>();

            // The director's Awake reads its canvases, so it's built inactive and wired before it wakes.
            GameObject directorObject = new("Director");
            directorObject.SetActive(false);
            directorObject.transform.SetParent(root.transform, false);
            MatchUIDirector director = directorObject.AddComponent<MatchUIDirector>();

            MainMenuScreen mainMenu = BuildMainMenu(root.transform);
            ConnectingScreen connecting = BuildConnecting(root.transform);
            HudScreen hud = BuildHud(root.transform);
            Canvas popupLayer = CreateCanvas(root.transform, "Popup Layer", sortingOrder: 100, enabled: true);
            DebugOverlay overlay = BuildDebugOverlay(root.transform);
            RoundEndPopup popupPrefab = BuildRoundEndPopupPrefab();

            SerializedObject directorFields = new(director);
            SetArray(directorFields.FindProperty("canvases"), mainMenu, connecting, hud);
            directorFields.FindProperty("popupLayer").objectReferenceValue = popupLayer.transform;
            directorFields.ApplyModifiedPropertiesWithoutUndo();
            directorObject.SetActive(true);

            SerializedObject uiFields = new(ui);
            uiFields.FindProperty("_director").objectReferenceValue = director;
            uiFields.FindProperty("_mainMenu").objectReferenceValue = mainMenu;
            uiFields.FindProperty("_connecting").objectReferenceValue = connecting;
            uiFields.FindProperty("_hud").objectReferenceValue = hud;
            uiFields.FindProperty("_debugOverlay").objectReferenceValue = overlay;
            uiFields.FindProperty("_roundEndPopupPrefab").objectReferenceValue = popupPrefab;
            uiFields.ApplyModifiedPropertiesWithoutUndo();
            return ui;
        }

        private static MainMenuScreen BuildMainMenu(Transform parent)
        {
            Canvas canvas = CreateCanvas(parent, "Main Menu", sortingOrder: 0, enabled: false);
            MainMenuScreen screen = canvas.gameObject.AddComponent<MainMenuScreen>();
            RectTransform panel = CreatePanel(canvas.transform, "Panel", new Vector2(760, 620), Vector2.zero);

            CreateText(panel, "Title", "NETCODE SAMPLE", 52, new Vector2(0, 240), new Vector2(700, 70), FontStyle.Bold);
            CreateText(panel, "Subtitle", "2-player peer-to-peer deterministic rollback", 24, new Vector2(0, 190), new Vector2(700, 40)).color = UIColors.Muted;

            CreateText(panel, "Port Label", "Port", 26, new Vector2(-250, 110), new Vector2(160, 50), anchor: TextAnchor.MiddleLeft);
            InputField port = CreateInputField(panel, "Port", new Vector2(-40, 110), new Vector2(240, 56));
            Button team = CreateButton(panel, "Team", "Host plays RED", new Vector2(-40, 30), new Vector2(440, 64), out Text teamLabel);
            Button host = CreateButton(panel, "Host", "HOST", new Vector2(250, 70), new Vector2(160, 144), out _);

            CreateText(panel, "Address Label", "Address", 26, new Vector2(-250, -80), new Vector2(160, 50), anchor: TextAnchor.MiddleLeft);
            InputField address = CreateInputField(panel, "Address", new Vector2(-40, -80), new Vector2(240, 56));
            Button join = CreateButton(panel, "Join", "JOIN", new Vector2(250, -80), new Vector2(160, 64), out _);

            Text message = CreateText(panel, "Message", string.Empty, 24, new Vector2(0, -200), new Vector2(700, 100));
            message.color = UIColors.Muted;
            CreateText(panel, "Hint", "Space spawns a big cube in the match. F1 shows the netcode overlay.", 20, new Vector2(0, -270), new Vector2(700, 40)).color = UIColors.Muted;

            Wire(screen, ("_addressField", address), ("_portField", port), ("_teamButton", team), ("_teamLabel", teamLabel),
                ("_hostButton", host), ("_joinButton", join), ("_messageText", message));
            SetUIObjects(screen, panel);
            return screen;
        }

        private static ConnectingScreen BuildConnecting(Transform parent)
        {
            Canvas canvas = CreateCanvas(parent, "Connecting", sortingOrder: 0, enabled: false);
            ConnectingScreen screen = canvas.gameObject.AddComponent<ConnectingScreen>();
            RectTransform panel = CreatePanel(canvas.transform, "Panel", new Vector2(760, 320), Vector2.zero);
            Text status = CreateText(panel, "Status", "Connecting...", 30, new Vector2(0, 50), new Vector2(700, 140));
            Button cancel = CreateButton(panel, "Cancel", "CANCEL", new Vector2(0, -100), new Vector2(240, 64), out _);
            Wire(screen, ("_statusText", status), ("_cancelButton", cancel));
            SetUIObjects(screen, panel);
            return screen;
        }

        private static HudScreen BuildHud(Transform parent)
        {
            Canvas canvas = CreateCanvas(parent, "HUD", sortingOrder: 0, enabled: false);
            HudScreen screen = canvas.gameObject.AddComponent<HudScreen>();

            RectTransform top = CreatePanel(canvas.transform, "Top Bar", new Vector2(1100, 120), new Vector2(0, -75), anchor: new Vector2(0.5f, 1f));
            Image redHealth = CreateHealthBar(top, "Red Base", UIColors.Red, new Vector2(-345, -10), Image.OriginHorizontal.Left);
            Image blueHealth = CreateHealthBar(top, "Blue Base", UIColors.Blue, new Vector2(345, -10), Image.OriginHorizontal.Right);
            CreateText(top, "Red Label", "RED BASE", 20, new Vector2(-345, 30), new Vector2(380, 30)).color = UIColors.Red;
            CreateText(top, "Blue Label", "BLUE BASE", 20, new Vector2(345, 30), new Vector2(380, 30)).color = UIColors.Blue;
            Text score = CreateText(top, "Score", "0 : 0", 44, new Vector2(0, 8), new Vector2(240, 60), FontStyle.Bold);
            score.supportRichText = true;
            Text round = CreateText(top, "Round", "Round 1", 20, new Vector2(0, -38), new Vector2(240, 30));
            round.color = UIColors.Muted;

            RectTransform bottom = CreatePanel(canvas.transform, "Bottom Bar", new Vector2(560, 110), new Vector2(0, 70), anchor: new Vector2(0.5f, 0f));
            Text you = CreateText(bottom, "You", "You are RED", 24, new Vector2(-170, 0), new Vector2(200, 40), FontStyle.Bold);
            Button big = CreateButton(bottom, "Big Cube", "BIG CUBE  [Space]", new Vector2(80, 0), new Vector2(260, 72), out Text bigLabel);
            Image cooldown = CreateImage(big.transform, "Cooldown", new Color(0f, 0f, 0f, 0.55f));
            Stretch(cooldown.rectTransform);
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Horizontal;
            cooldown.fillOrigin = (int)Image.OriginHorizontal.Right;
            cooldown.fillAmount = 0f;
            cooldown.raycastTarget = false;
            bigLabel.transform.SetAsLastSibling();

            Button leave = CreateButton(canvas.transform, "Leave", "LEAVE", new Vector2(-90, -50), new Vector2(140, 50), out _, anchor: new Vector2(1f, 1f));

            Wire(screen, ("_scoreText", score), ("_roundText", round), ("_youText", you), ("_redHealthFill", redHealth),
                ("_blueHealthFill", blueHealth), ("_bigButton", big), ("_bigCooldownFill", cooldown), ("_bigLabel", bigLabel), ("_leaveButton", leave));
            SetUIObjects(screen, top, bottom);
            return screen;
        }

        private static DebugOverlay BuildDebugOverlay(Transform parent)
        {
            Canvas canvas = CreateCanvas(parent, "Debug Overlay (F1)", sortingOrder: 50, enabled: false);
            DebugOverlay overlay = canvas.gameObject.AddComponent<DebugOverlay>();
            RectTransform panel = CreatePanel(canvas.transform, "Panel", new Vector2(720, 420), new Vector2(380, -330), anchor: new Vector2(0f, 1f));

            Text stats = CreateText(panel, "Stats", string.Empty, 20, new Vector2(0, 110), new Vector2(680, 170), anchor: TextAnchor.UpperLeft);
            Toggle toggle = CreateToggle(panel, "Latency Toggle", "Simulate latency on this peer's outgoing traffic (FishNet)", new Vector2(0, 5));
            Text conditions = CreateText(panel, "Conditions", string.Empty, 20, new Vector2(0, -35), new Vector2(680, 30), anchor: TextAnchor.MiddleLeft);
            Slider latency = CreateSlider(panel, "Latency", "latency", new Vector2(0, -75));
            Slider loss = CreateSlider(panel, "Packet Loss", "loss", new Vector2(0, -115));
            Slider outOfOrder = CreateSlider(panel, "Out Of Order", "order", new Vector2(0, -155));
            Button chaos = CreateButton(panel, "Chaos", "Plant chaos on this peer", new Vector2(0, -190), new Vector2(680, 44), out Text chaosLabel);

            Wire(overlay, ("_canvas", canvas), ("_statsText", stats), ("_latencyToggle", toggle), ("_latencySlider", latency),
                ("_lossSlider", loss), ("_outOfOrderSlider", outOfOrder), ("_conditionsText", conditions), ("_chaosButton", chaos), ("_chaosLabel", chaosLabel));
            return overlay;
        }

        private static RoundEndPopup BuildRoundEndPopupPrefab()
        {
            RoundEndPopup existing = AssetDatabase.LoadAssetAtPath<RoundEndPopup>(PopupPrefabPath);
            if (existing != null)
            {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(UIFolder))
            {
                AssetDatabase.CreateFolder("Assets/NetcodeSample", "UI");
            }

            GameObject root = new("Round End Popup", typeof(RectTransform));
            Stretch((RectTransform)root.transform);
            RoundEndPopup popup = root.AddComponent<RoundEndPopup>();
            RectTransform panel = CreatePanel(root.transform, "Panel", new Vector2(620, 260), new Vector2(0, 60));
            Text title = CreateText(panel, "Title", "ROUND WON", 56, new Vector2(0, 50), new Vector2(580, 80), FontStyle.Bold);
            Text detail = CreateText(panel, "Detail", "1 : 0\nNext round in 3", 28, new Vector2(0, -50), new Vector2(580, 100));
            Wire(popup, ("_titleText", title), ("_detailText", detail));

            RoundEndPopup prefab = PrefabUtility.SaveAsPrefabAsset(root, PopupPrefabPath).GetComponent<RoundEndPopup>();
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null)
            {
                GameObject eventObject = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(eventObject, "Build Match UI");
                return;
            }

            // The project uses the Input System only; the legacy module would throw.
            StandaloneInputModule legacy = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacy != null)
            {
                Undo.DestroyObjectImmediate(legacy);
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                Undo.AddComponent<InputSystemUIInputModule>(eventSystem.gameObject);
            }
        }

        private static Canvas CreateCanvas(Transform parent, string name, int sortingOrder, bool enabled)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            gameObject.transform.SetParent(parent, false);
            Canvas canvas = gameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            // HannibalUI screens start disabled; activation enables them.
            canvas.enabled = enabled;
            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = s_referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        // A dark rounded panel that scales in when its screen activates (a HannibalUI UI object).
        private static RectTransform CreatePanel(Transform parent, string name, Vector2 size, Vector2 position, Vector2? anchor = null)
        {
            Image image = CreateImage(parent, name, UIColors.Panel);
            image.sprite = s_resources.background;
            image.type = Image.Type.Sliced;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor ?? new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            VP_UIObject uiObject = image.gameObject.AddComponent<VP_UIObject>();
            SerializedObject fields = new(uiObject);
            SerializedProperty animations = fields.FindProperty("_animationComponents");
            animations.arraySize = 1;
            animations.GetArrayElementAtIndex(0).managedReferenceValue = new ScaleAnimationComponent();
            fields.ApplyModifiedPropertiesWithoutUndo();
            return rect;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string content, int size, Vector2 position, Vector2 dimensions,
            FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            Text text = gameObject.GetComponent<Text>();
            text.font = s_font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = UIColors.Text;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            RectTransform rect = text.rectTransform;
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 position, Vector2 size, out Text labelText, Vector2? anchor = null)
        {
            GameObject gameObject = DefaultControls.CreateButton(s_resources);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.anchorMin = rect.anchorMax = anchor ?? new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            gameObject.GetComponent<Image>().color = UIColors.Button;

            labelText = gameObject.GetComponentInChildren<Text>();
            labelText.font = s_font;
            labelText.text = label;
            labelText.fontSize = 24;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = UIColors.Text;
            return gameObject.GetComponent<Button>();
        }

        private static InputField CreateInputField(Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject gameObject = DefaultControls.CreateInputField(s_resources);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            foreach (Text text in gameObject.GetComponentsInChildren<Text>(true))
            {
                text.font = s_font;
                text.fontSize = 24;
            }

            return gameObject.GetComponent<InputField>();
        }

        private static Toggle CreateToggle(Transform parent, string name, string label, Vector2 position)
        {
            GameObject gameObject = DefaultControls.CreateToggle(s_resources);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.sizeDelta = new Vector2(680, 30);
            rect.anchoredPosition = position;
            Text text = gameObject.GetComponentInChildren<Text>();
            text.font = s_font;
            text.text = label;
            text.fontSize = 20;
            text.color = UIColors.Text;
            Toggle toggle = gameObject.GetComponent<Toggle>();
            toggle.isOn = false;
            return toggle;
        }

        private static Slider CreateSlider(Transform parent, string name, string label, Vector2 position)
        {
            CreateText(parent, name + " Label", label, 18, position + new Vector2(-280, 0), new Vector2(110, 30), anchor: TextAnchor.MiddleLeft).color = UIColors.Muted;
            GameObject gameObject = DefaultControls.CreateSlider(s_resources);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.sizeDelta = new Vector2(540, 20);
            rect.anchoredPosition = position + new Vector2(60, 0);
            return gameObject.GetComponent<Slider>();
        }

        private static Image CreateHealthBar(Transform parent, string name, Color color, Vector2 position, Image.OriginHorizontal origin)
        {
            Image background = CreateImage(parent, name + " Health", new Color(0f, 0f, 0f, 0.6f));
            background.rectTransform.sizeDelta = new Vector2(380, 26);
            background.rectTransform.anchoredPosition = position;
            Image fill = CreateImage(background.transform, "Fill", color);
            Stretch(fill.rectTransform);
            fill.sprite = s_resources.standard;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)origin;
            fill.fillAmount = 1f;
            return fill;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Wire(Object target, params (string Property, Object Value)[] fields)
        {
            SerializedObject serialized = new(target);
            foreach ((string property, Object value) in fields)
            {
                SerializedProperty field = serialized.FindProperty(property);
                if (field == null)
                {
                    Debug.LogError($"MatchUIBuilder: {target.GetType().Name} has no field {property}.");
                    continue;
                }

                field.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetUIObjects(VP_Canvas screen, params RectTransform[] panels)
        {
            VP_UIObject[] uiObjects = new VP_UIObject[panels.Length];
            for (int i = 0; i < panels.Length; i++)
            {
                uiObjects[i] = panels[i].GetComponent<VP_UIObject>();

                // Hidden until the screen activates and scales it in.
                panels[i].localScale = Vector3.zero;
            }

            SerializedObject serialized = new(screen);
            SetArray(serialized.FindProperty("uIObjects"), uiObjects);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(SerializedProperty array, params Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            array.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
