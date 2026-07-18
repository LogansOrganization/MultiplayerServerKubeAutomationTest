using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// First thing the player sees. Deliberately does nothing with NetworkManager —
// StartClient() must not be called until the player picks a server and hits
// Connect in ServerBrowserUI. Built entirely from code at runtime, same
// pattern as ServerBrowserUI, so it can be dropped in without any
// prefab/.unity scene wiring.
public class TitleScreenUI : MonoBehaviour
{
    [SerializeField] private string gameTitle = "Fantasy RP";

    private Action onPlayPressed;

    public static TitleScreenUI Show(Action onPlayPressed)
    {
        GameObject go = new GameObject(nameof(TitleScreenUI));
        TitleScreenUI title = go.AddComponent<TitleScreenUI>();
        title.onPlayPressed = onPlayPressed;
        return title;
    }

    private void Awake()
    {
        BuildUI();
    }

    private void OnPlayClicked()
    {
        Action callback = onPlayPressed;
        Destroy(gameObject);
        callback?.Invoke();
    }

    private Text CreateText(Transform parent, string content, TextAnchor alignment)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        Text text = go.GetComponent<Text>();
        text.text = content;
        text.alignment = alignment;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 18;
        return text;
    }

    private void BuildUI()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            // This project's Active Input Handling is set to the new Input
            // System package only (ProjectSettings activeInputHandler: 1), so
            // the legacy StandaloneInputModule can't read input at all —
            // needs InputSystemUIInputModule instead.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        GameObject canvasGo = new GameObject("TitleScreenCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject backgroundGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backgroundGo.transform.SetParent(canvasGo.transform, false);
        RectTransform backgroundRect = backgroundGo.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.sizeDelta = Vector2.zero;
        backgroundRect.anchoredPosition = Vector2.zero;
        backgroundGo.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 1f);

        GameObject panelGo = new GameObject("Panel", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelGo.transform.SetParent(canvasGo.transform, false);
        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(400f, 200f);

        VerticalLayoutGroup panelLayout = panelGo.GetComponent<VerticalLayoutGroup>();
        panelLayout.spacing = 20f;
        panelLayout.childAlignment = TextAnchor.MiddleCenter;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        ContentSizeFitter panelFitter = panelGo.GetComponent<ContentSizeFitter>();
        panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        panelFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        GameObject titleGo = new GameObject("Title", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        titleGo.transform.SetParent(panelGo.transform, false);
        Text title = titleGo.GetComponent<Text>();
        title.text = gameTitle;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        title.fontSize = 48;
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleGo.GetComponent<LayoutElement>().minHeight = 60f;

        GameObject buttonGo = new GameObject("PlayButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonGo.transform.SetParent(panelGo.transform, false);
        Image buttonImage = buttonGo.GetComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.5f, 0.9f);
        buttonGo.GetComponent<LayoutElement>().minHeight = 50f;
        buttonGo.GetComponent<LayoutElement>().minWidth = 200f;
        Button playButton = buttonGo.GetComponent<Button>();
        playButton.targetGraphic = buttonImage;
        CreateText(buttonGo.transform, "Play", TextAnchor.MiddleCenter);
        playButton.onClick.AddListener(OnPlayClicked);
    }
}
