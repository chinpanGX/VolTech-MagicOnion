using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// サンプルの画面をコードで組み立てる。Prefab やアセットを増やさない。
    /// </summary>
    public sealed class SampleUi
    {
        private const int FontSize = 28;

        private readonly Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public SampleUi(Transform parent)
        {
            new GameObject("Camera", typeof(Camera)).transform.SetParent(parent);

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(parent);
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            Title = CreatePanel("Title", canvasObject.transform, new Vector2(0, 0), new Vector2(1, 0.85f));
            StartButton = CreateButton("はじめる", Title.transform);

            Home = CreatePanel("Home", canvasObject.transform, new Vector2(0, 0), new Vector2(1, 0.85f));
            GemsText = CreateText(Home.transform);
            ResultText = CreateText(Home.transform);
            FreeButton = CreateButton("無料で引く", Home.transform);
            PaidButton = CreateButton("引く(300 ジェム)", Home.transform);

            Band = CreatePanel("Band", canvasObject.transform, new Vector2(0, 0.85f), new Vector2(1, 1));
            Band.AddComponent<Image>().color = new Color(0.6f, 0.15f, 0.15f);
            var bandLayout = Band.GetComponent<VerticalLayoutGroup>();
            bandLayout.padding = new RectOffset(16, 16, 8, 8);
            BandText = CreateText(Band.transform);
            BandFirstButton = CreateButton("", Band.transform);
            BandSecondButton = CreateButton("", Band.transform);
            Band.SetActive(false);
        }

        public GameObject Title { get; }

        public Button StartButton { get; }

        public GameObject Home { get; }

        public Text GemsText { get; }

        public Text ResultText { get; }

        public Button FreeButton { get; }

        public Button PaidButton { get; }

        public GameObject Band { get; }

        public Text BandText { get; }

        public Button BandFirstButton { get; }

        public Button BandSecondButton { get; }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 8;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return panel;
        }

        private Text CreateText(Transform parent)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            textObject.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 40);
            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = FontSize;
            text.alignment = TextAnchor.MiddleCenter;
            return text;
        }

        private Button CreateButton(string label, Transform parent)
        {
            var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<RectTransform>().sizeDelta = new Vector2(360, 56);

            var text = CreateText(buttonObject.transform);
            var rect = text.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            text.color = Color.black;
            text.text = label;
            return buttonObject.GetComponent<Button>();
        }
    }
}
