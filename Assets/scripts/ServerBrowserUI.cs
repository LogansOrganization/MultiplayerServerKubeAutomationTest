using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Minimal server browser: lists GameServers registered with the relay (via
// ServerListClient's GET /servers) and lets the player pick one to connect
// to. Built entirely from code at runtime (no prefab/.unity scene wiring
// required) — drop this script's Show() call anywhere and it constructs its
// own Canvas/EventSystem/ScrollView.
public class ServerBrowserUI : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 5f;
    private const float RowHeight = 40f;

    [SerializeField] private string relayHttpBaseUrl = "http://192.168.10.228:8080";

    private Action<string, ushort> onServerSelected;
    private ServerListClient listClient;
    private RectTransform listContainer;
    private Text statusText;

    public static ServerBrowserUI Show(Action<string, ushort> onServerSelected)
    {
        GameObject go = new GameObject(nameof(ServerBrowserUI));
        ServerBrowserUI browser = go.AddComponent<ServerBrowserUI>();
        browser.onServerSelected = onServerSelected;
        return browser;
    }

    private void Awake()
    {
        listClient = gameObject.AddComponent<ServerListClient>();
        BuildUI();
    }

    private void Start()
    {
        StartCoroutine(RefreshLoop());
    }

    private IEnumerator RefreshLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(RefreshIntervalSeconds);
        while (true)
        {
            yield return Refresh();
            yield return wait;
        }
    }

    private IEnumerator Refresh()
    {
        statusText.text = "Refreshing...";
        yield return listClient.FetchServers(relayHttpBaseUrl, OnServersFetched, OnFetchError);
    }

    private void OnServersFetched(List<ServerListClient.ServerListing> servers)
    {
        foreach (Transform child in listContainer)
        {
            Destroy(child.gameObject);
        }

        if (servers.Count == 0)
        {
            statusText.text = "No servers available.";
            return;
        }

        statusText.text = $"{servers.Count} server(s) available.";

        foreach (ServerListClient.ServerListing server in servers)
        {
            CreateRow(server);
        }
    }

    private void OnFetchError(string error)
    {
        statusText.text = $"Failed to reach relay: {error}";
    }

    private void CreateRow(ServerListClient.ServerListing server)
    {
        GameObject row = new GameObject($"Row_{server.name}", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(listContainer, false);
        row.GetComponent<LayoutElement>().minHeight = RowHeight;

        HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.padding = new RectOffset(10, 10, 5, 5);
        rowLayout.spacing = 10f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        Text label = CreateText(row.transform, $"{server.name}  ({server.player_count}/{server.max_players})", TextAnchor.MiddleLeft);
        label.GetComponent<LayoutElement>().flexibleWidth = 1f;

        GameObject buttonGo = new GameObject("ConnectButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonGo.transform.SetParent(row.transform, false);
        Image buttonImage = buttonGo.GetComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.5f, 0.9f);
        buttonGo.GetComponent<LayoutElement>().minWidth = 100f;
        Button connectButton = buttonGo.GetComponent<Button>();
        connectButton.targetGraphic = buttonImage;
        CreateText(buttonGo.transform, "Connect", TextAnchor.MiddleCenter);

        string connectAddress = server.connect_address;
        connectButton.onClick.AddListener(() => Connect(connectAddress));
    }

    private void Connect(string connectAddress)
    {
        int separatorIndex = connectAddress.LastIndexOf(':');
        if (separatorIndex < 0 || !ushort.TryParse(connectAddress.Substring(separatorIndex + 1), out ushort port))
        {
            statusText.text = $"Malformed connect address: {connectAddress}";
            return;
        }

        string ip = connectAddress.Substring(0, separatorIndex);
        onServerSelected?.Invoke(ip, port);
        Destroy(gameObject);
    }

    // Creates a Text that stretches to fill whatever it's parented under. When
    // the parent has its own layout group (e.g. a row's HorizontalLayoutGroup)
    // that group takes over sizing/position each layout pass anyway; when the
    // parent is a plain leaf (e.g. a button), this stretch is what makes the
    // label fill the button.
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

        GameObject canvasGo = new GameObject("ServerBrowserCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelGo.transform.SetParent(canvasGo.transform, false);
        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(500f, 400f);
        panelGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        VerticalLayoutGroup panelLayout = panelGo.GetComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(10, 10, 10, 10);
        panelLayout.spacing = 5f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;
        // Panel height is fixed (sizeDelta above), not fit-to-content — the
        // ContentSizeFitter component is only here because VerticalLayoutGroup
        // requires one present to report a preferred size upward; both axes
        // stay Unconstrained so the explicit sizeDelta wins.
        ContentSizeFitter panelFitter = panelGo.GetComponent<ContentSizeFitter>();
        panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        panelFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        GameObject titleGo = new GameObject("Title", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        titleGo.transform.SetParent(panelGo.transform, false);
        Text title = titleGo.GetComponent<Text>();
        title.text = "Servers";
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        title.fontSize = 24;
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleGo.GetComponent<LayoutElement>().minHeight = 30f;

        GameObject statusGo = new GameObject("Status", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        statusGo.transform.SetParent(panelGo.transform, false);
        statusText = statusGo.GetComponent<Text>();
        statusText.text = "Loading...";
        statusText.alignment = TextAnchor.MiddleCenter;
        statusText.color = Color.white;
        statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize = 14;
        statusGo.GetComponent<LayoutElement>().minHeight = 24f;

        GameObject scrollGo = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(LayoutElement));
        scrollGo.transform.SetParent(panelGo.transform, false);
        scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        scrollGo.GetComponent<LayoutElement>().flexibleHeight = 1f;

        GameObject viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewportRect.anchoredPosition = Vector2.zero;

        GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        listContainer = contentGo.GetComponent<RectTransform>();
        listContainer.anchorMin = new Vector2(0f, 1f);
        listContainer.anchorMax = new Vector2(1f, 1f);
        listContainer.pivot = new Vector2(0.5f, 1f);
        listContainer.anchoredPosition = Vector2.zero;
        listContainer.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 2f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        ContentSizeFitter contentFitter = contentGo.GetComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = scrollGo.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = listContainer;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        GameObject refreshGo = new GameObject("RefreshButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        refreshGo.transform.SetParent(panelGo.transform, false);
        Image refreshImage = refreshGo.GetComponent<Image>();
        refreshImage.color = new Color(0.3f, 0.3f, 0.3f);
        refreshGo.GetComponent<LayoutElement>().minHeight = 30f;
        Button refreshButton = refreshGo.GetComponent<Button>();
        refreshButton.targetGraphic = refreshImage;
        CreateText(refreshGo.transform, "Refresh", TextAnchor.MiddleCenter);
        refreshButton.onClick.AddListener(() => StartCoroutine(Refresh()));
    }
}
