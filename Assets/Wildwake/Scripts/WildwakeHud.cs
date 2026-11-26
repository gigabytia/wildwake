using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Wildwake
{
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas), typeof(CanvasScaler))]
    public sealed class WildwakeHud : MonoBehaviour
    {
        private const float DamageLifetime = 0.85f;
        public const string ControlsHint = "WASD  движение    Мышь  камера    Shift  бег    Пробел ×2  прыжок    C  подкат    ЛКМ  удар";

        private static readonly Color Panel = new Color32(15, 23, 22, 235);
        private static readonly Color Track = new Color32(43, 52, 49, 255);
        private static readonly Color Edge = new Color32(91, 105, 92, 255);
        private static readonly Color Ink = new Color32(245, 239, 221, 255);
        private static readonly Color MutedInk = new Color32(200, 211, 195, 255);
        private static readonly Color Red = new Color32(225, 95, 87, 255);
        private static readonly Color Green = new Color32(139, 203, 154, 255);
        private static readonly Color Gold = new Color32(249, 202, 128, 255);
        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        private static WildwakeHud activeHud;

        [SerializeField] private WildwakePlayer player;
        [SerializeField] private WildwakeEnemy enemy;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform enemyHealthAnchor;
        [SerializeField] private Font uiFont;

        [SerializeField] private RectTransform playerHealth;
        [SerializeField] private RectTransform playerStamina;
        [SerializeField] private RectTransform enemyMarker;
        [SerializeField] private RectTransform enemyHealth;
        [SerializeField] private RectTransform enemyStamina;
        [SerializeField] private Text enemyStatus;
        [SerializeField] private GameObject pickupPrompt;
        [SerializeField] private GameObject outcomePanel;
        [SerializeField] private Text outcomeTitle;
        [SerializeField] private Text restartHint;

        private readonly List<FloatingDamage> damageNumbers = new List<FloatingDamage>();
        private RectTransform canvasRect;
        private bool encounterEnded;

        private struct FloatingDamage
        {
            public Text Label;
            public Vector3 Position;
            public Vector2 Drift;
            public Color Color;
            public float StartTime;
        }

        public void Configure(WildwakePlayer controlledPlayer, WildwakeEnemy targetEnemy,
            Camera camera, Transform healthAnchor)
        {
            player = controlledPlayer;
            enemy = targetEnemy;
            viewCamera = camera;
            enemyHealthAnchor = healthAnchor;
            if (!playerHealth)
                BuildInterface();
        }

        private void Awake()
        {
            canvasRect = (RectTransform)transform;
            if (!playerHealth)
                BuildInterface();
            else if (!uiFont)
                uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void OnEnable()
        {
            activeHud = this;
        }

        private void OnDisable()
        {
            if (activeHud == this)
                activeHud = null;
        }

        private void LateUpdate()
        {
            if (player)
            {
                SetFill(playerHealth, player.HealthNormalized);
                SetFill(playerStamina, player.StaminaNormalized);
                SetVisible(pickupPrompt, !encounterEnded && player.CanPickUpWeapon);
            }

            UpdateEnemyMarker();
            UpdateFloatingDamage();
            UpdateOutcome();
        }

        private void UpdateEnemyMarker()
        {
            if (!enemy || enemy.IsDead)
            {
                SetVisible(enemyMarker.gameObject, false);
                return;
            }

            Vector3 point = enemyHealthAnchor ? enemyHealthAnchor.position : enemy.transform.position + Vector3.up * 2.5f;
            bool visible = TryProject(point, out Vector2 position);
            SetVisible(enemyMarker.gameObject, visible);
            if (!visible)
                return;

            enemyMarker.anchoredPosition = position + Vector2.up * 9f;
            SetFill(enemyHealth, enemy.HealthNormalized);
            SetFill(enemyStamina, enemy.StaminaNormalized);
            enemyStatus.text = enemy.IsStunned ? "ОГЛУШЁН" : "ДУХ";
            enemyStatus.color = enemy.IsStunned ? Gold : Ink;
        }

        private void UpdateOutcome()
        {
            if (!encounterEnded)
            {
                bool playerLost = player && player.IsDead;
                if (!playerLost && (!enemy || !enemy.IsDead))
                    return;

                encounterEnded = true;
                outcomeTitle.text = playerLost ? "Поражение" : "Дух повержен";
                SetVisible(restartHint.gameObject, gameObject.scene.buildIndex >= 0);
                SetVisible(outcomePanel, true);
                SetVisible(pickupPrompt, false);
            }

            var keyboard = Keyboard.current;
            if (gameObject.scene.buildIndex >= 0 && keyboard != null && keyboard.rKey.wasPressedThisFrame)
                SceneManager.LoadScene(gameObject.scene.buildIndex);
        }

        public static void ShowDamage(Vector3 worldPosition, float amount, bool playerWasHit)
        {
            if (activeHud && amount > 0f)
                activeHud.AddFloatingDamage(worldPosition, amount, playerWasHit);
        }

        private void AddFloatingDamage(Vector3 worldPosition, float amount, bool playerWasHit)
        {
            Color highlight = playerWasHit ? new Color32(255, 142, 130, 255) : Gold;
            RectTransform parent = (RectTransform)transform;
            Text label = CreateLabel(parent, "Damage", Mathf.Max(1, Mathf.RoundToInt(amount)).ToString(),
                28, highlight, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.one * 0.5f,
                Vector2.one * 0.5f, Vector2.zero, new Vector2(90f, 40f));
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color32(19, 13, 13, 255);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            damageNumbers.Add(new FloatingDamage
            {
                Label = label,
                Position = worldPosition,
                Drift = new Vector2(damageNumbers.Count % 2 == 0 ? 25f : -25f, 52f),
                Color = highlight,
                StartTime = Time.unscaledTime
            });
            SetVisible(label.gameObject, false);
        }

        private void UpdateFloatingDamage()
        {
            for (int index = damageNumbers.Count - 1; index >= 0; index--)
            {
                var number = damageNumbers[index];
                float age = (Time.unscaledTime - number.StartTime) / DamageLifetime;
                if (age >= 1f)
                {
                    Destroy(number.Label.gameObject);
                    damageNumbers.RemoveAt(index);
                    continue;
                }

                bool visible = TryProject(number.Position, out Vector2 position);
                SetVisible(number.Label.gameObject, visible);
                if (!visible)
                    continue;

                RectTransform rect = number.Label.rectTransform;
                rect.anchoredPosition = position + number.Drift * (1f - (1f - age) * (1f - age));
                rect.localScale = Vector3.one * Mathf.Lerp(1.2f, 1f, Mathf.Min(1f, age * 5f));
                Color color = number.Color;
                color.a = 1f - Mathf.InverseLerp(0.38f, 1f, age);
                number.Label.color = color;
            }
        }

        private bool TryProject(Vector3 worldPoint, out Vector2 canvasPoint)
        {
            canvasPoint = Vector2.zero;
            if (!viewCamera)
                return false;

            Vector3 screenPoint = viewCamera.WorldToScreenPoint(worldPoint);
            return screenPoint.z > viewCamera.nearClipPlane && viewCamera.pixelRect.Contains(screenPoint)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out canvasPoint);
        }

        private void BuildInterface()
        {
            canvasRect = (RectTransform)transform;
            Canvas canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Vector2 center = Vector2.one * 0.5f;
            Vector2 bottomCenter = new Vector2(0.5f, 0f);

            RectTransform vitals = CreateBox(canvasRect, "Player vitals", Panel, TopLeft, TopLeft,
                new Vector2(28f, -28f), new Vector2(278f, 102f));
            CreateBox(vitals, "Accent", Gold, TopLeft, TopLeft, Vector2.zero, new Vector2(3f, 102f));
            CreateLabel(vitals, "Health label", "ЗДОРОВЬЕ", 13, Ink, FontStyle.Bold, TextAnchor.MiddleLeft,
                TopLeft, TopLeft, new Vector2(16f, -10f), new Vector2(246f, 18f));
            playerHealth = CreateBar(vitals, "Health", Red, TopLeft, TopLeft,
                new Vector2(16f, -32f), new Vector2(246f, 12f));
            CreateLabel(vitals, "Stamina label", "ВЫНОСЛИВОСТЬ", 13, Ink, FontStyle.Bold, TextAnchor.MiddleLeft,
                TopLeft, TopLeft, new Vector2(16f, -55f), new Vector2(246f, 18f));
            playerStamina = CreateBar(vitals, "Stamina", Green, TopLeft, TopLeft,
                new Vector2(16f, -77f), new Vector2(246f, 9f));

            enemyMarker = CreateBox(canvasRect, "Enemy overhead", Panel, center, bottomCenter,
                Vector2.zero, new Vector2(126f, 33f));
            enemyStatus = CreateLabel(enemyMarker, "Enemy status", "ДУХ", 12, Ink, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, new Vector2(5f, -2f), new Vector2(116f, 15f));
            enemyHealth = CreateBar(enemyMarker, "Enemy HP", Red, TopLeft, TopLeft,
                new Vector2(8f, -18f), new Vector2(110f, 6f));
            enemyStamina = CreateBar(enemyMarker, "Enemy stamina", Green, TopLeft, TopLeft,
                new Vector2(8f, -26f), new Vector2(110f, 4f));
            SetVisible(enemyMarker.gameObject, false);

            RectTransform prompt = CreateBox(canvasRect, "Weapon pickup", Panel, bottomCenter, bottomCenter,
                new Vector2(0f, 115f), new Vector2(220f, 45f));
            CreateBox(prompt, "Key backing", Edge, TopLeft, TopLeft, new Vector2(9f, -8f), new Vector2(29f, 29f));
            CreateLabel(prompt, "Pickup key", "F", 18, Ink, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, new Vector2(9f, -8f), new Vector2(29f, 29f));
            CreateLabel(prompt, "Pickup action", "Подобрать оружие", 17, Ink, FontStyle.Normal, TextAnchor.MiddleLeft,
                TopLeft, TopLeft, new Vector2(48f, -8f), new Vector2(163f, 29f));
            pickupPrompt = prompt.gameObject;
            SetVisible(pickupPrompt, false);

            RectTransform controls = CreateBox(canvasRect, "Controls", Panel, Vector2.zero, Vector2.zero,
                new Vector2(28f, 28f), new Vector2(760f, 32f));
            CreateLabel(controls, "Controls label", ControlsHint, 15, MutedInk,
                FontStyle.Normal, TextAnchor.MiddleLeft, TopLeft, TopLeft,
                new Vector2(12f, 0f), new Vector2(736f, 32f));

            RectTransform outcome = CreateBox(canvasRect, "Encounter outcome", Panel, center, center,
                new Vector2(0f, -40f), new Vector2(350f, 94f));
            CreateBox(outcome, "Accent", Gold, TopLeft, TopLeft, Vector2.zero, new Vector2(350f, 2f));
            outcomeTitle = CreateLabel(outcome, "Outcome title", string.Empty, 28, Ink, FontStyle.Bold, TextAnchor.MiddleCenter,
                TopLeft, TopLeft, new Vector2(12f, -10f), new Vector2(326f, 43f));
            restartHint = CreateLabel(outcome, "Restart hint", "R  Начать заново", 16, MutedInk, FontStyle.Normal,
                TextAnchor.MiddleCenter, TopLeft, TopLeft, new Vector2(12f, -54f), new Vector2(326f, 25f));
            outcomePanel = outcome.gameObject;
            SetVisible(outcomePanel, false);
        }

        private static RectTransform CreateBar(RectTransform parent, string name, Color fillColor, Vector2 anchor,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform track = CreateBox(parent, name, Track, anchor, pivot, position, size);
            RectTransform fill = CreateBox(track, "Fill", fillColor, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            fill.anchorMax = Vector2.one;
            fill.offsetMin = new Vector2(1f, 1f);
            fill.offsetMax = new Vector2(-1f, -1f);
            return fill;
        }

        private static void SetFill(RectTransform fill, float value)
        {
            float ratio = Mathf.Clamp01(value);
            SetVisible(fill.gameObject, ratio > 0f);
            if (fill.localScale.x != ratio)
                fill.localScale = new Vector3(ratio, 1f, 1f);
        }

        private static void SetVisible(GameObject item, bool visible)
        {
            if (item.activeSelf != visible)
                item.SetActive(visible);
        }

        private static RectTransform CreateBox(RectTransform parent, string name, Color color, Vector2 anchor,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(parent, name, anchor, pivot, position, size);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private Text CreateLabel(RectTransform parent, string name, string content, int fontSize, Color color,
            FontStyle weight, TextAnchor alignment, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(parent, name, anchor, pivot, position, size);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = uiFont;
            label.text = content;
            label.fontSize = fontSize;
            label.fontStyle = weight;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform CreateRect(RectTransform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
