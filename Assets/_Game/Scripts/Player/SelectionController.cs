using AnimalFarm.Core;
using AnimalFarm.Interaction;
using AnimalFarm.Spirits;
using AnimalFarm.UI;
using AnimalFarm.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimalFarm.Player
{
    /// <summary>
    /// Click selection (mouse-side companion to the walk-up IInteractable flow).
    /// Left click on anything with a Collider2D whose hierarchy implements
    /// ISelectable opens the SelectionMenuUI context menu. Also owns the two
    /// ghost modes: home MOVE mode and new-home PLACEMENT mode (entered from
    /// the build menu). In both, a ghost sprite follows the cursor snapped to
    /// grid cells until the player confirms (left click on a valid cell) or
    /// cancels (right click). Lives on the camera or a manager object.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class SelectionController : MonoBehaviour
    {
        public static SelectionController Instance { get; private set; }

        /// <summary>
        /// Frame stamp of the last click consumed by selection/placement, so
        /// direct mouse readers (ToolController) do not also act on that click.
        /// </summary>
        public static int ConsumedClickFrame = -1;

        private static readonly Color GhostValid = new Color(0.35f, 1f, 0.45f, 0.55f);
        private static readonly Color GhostInvalid = new Color(1f, 0.35f, 0.3f, 0.55f);

        /// <summary>True while a ghost mode owns the cursor (home move mode or
        /// placement mode) -- tools stay suppressed either way.</summary>
        public bool IsMoving => _movingHome != null || _placing;

        private Home _movingHome;
        private bool _placing;
        private System.Func<Vector2Int, bool> _placeValidate;
        private System.Action<Vector2Int, Vector3> _placeOnPlace;
        private System.Action _placeOnCancel;
        private SpriteRenderer _ghost;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DestroyGhost();
        }

        private void Update()
        {
            if (_movingHome != null)
            {
                UpdateMoveMode();
                return; // move mode suppresses normal selection clicks
            }

            if (_placing)
            {
                UpdatePlaceMode();
                return; // placement mode suppresses normal selection clicks
            }

            // The home may have been destroyed mid-move; tidy the ghost.
            if (_ghost != null) DestroyGhost();

            HandleSelectionClick();
        }

        // ---- selection ---------------------------------------------------------

        private void HandleSelectionClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (UIInputLock.BlockDirectKeys) return;

            var comp = AnimalFarm.Competitions.CompetitionManager.Instance;
            if (comp != null && comp.EventRunning) return;

            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem != null && eventSystem.IsPointerOverGameObject()) return;

            var cam = Camera.main;
            if (cam == null) return;

            Vector2 screenPos = mouse.position.ReadValue();
            Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            worldPos.z = 0f;

            ISelectable selectable = null;
            var hits = Physics2D.OverlapPointAll(worldPos);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null) continue;
                // Checks the collider's own GameObject first, then its parents.
                var found = hits[i].GetComponentInParent<ISelectable>();
                if (found != null) { selectable = found; break; }
            }

            if (selectable != null)
            {
                ConsumedClickFrame = Time.frameCount;
                var menu = EnsureMenu();
                if (menu != null) menu.Open(selectable, screenPos);
            }
            else
            {
                var menu = SelectionMenuUI.Instance;
                if (menu != null && menu.IsOpen) menu.Close(); // click-away
            }
        }

        private static SelectionMenuUI EnsureMenu()
        {
            if (SelectionMenuUI.Instance != null) return SelectionMenuUI.Instance;
            var go = new GameObject("SelectionMenuUI");
            return go.AddComponent<SelectionMenuUI>();
        }

        // ---- move mode ---------------------------------------------------------

        /// <summary>Enters home move mode: ghost follows the cursor cell by cell.</summary>
        public void BeginMove(Home home)
        {
            if (home == null) return;
            _movingHome = home;
            _placing = false; // move mode and placement mode are exclusive

            var menu = SelectionMenuUI.Instance;
            if (menu != null && menu.IsOpen) menu.Close();

            var source = home.GetComponent<SpriteRenderer>();
            StartGhost(source != null ? source.sprite : null,
                home.transform.localScale, home.transform.position);
        }

        // ---- placement mode ------------------------------------------------------

        /// <summary>
        /// Enters new-home placement mode (from the build menu): a ghost of the
        /// species' home follows the cursor showing green/red validity; left
        /// click places ONE home via HomeManager, right click cancels.
        /// </summary>
        public void BeginPlaceHome(SpiritSpeciesDefinition species)
        {
            if (species == null || species.homeSprite == null) return;

            // 2.2 matches HomeManager.Spawn's home scale.
            BeginPlaceBuilding(species.homeSprite, 2.2f, null, (cell, world) =>
            {
                if (HomeManager.Instance == null) return;
                var home = HomeManager.Instance.PlaceHome(species, cell);
                if (home != null)
                {
                    FloatingText.Show(world, species.displayName + " home built", UIStyle.Gold);
                    AnimalFarm.Core.ShepherdProgress.Grant("build");
                    AnimalFarm.World.VendorArrivals.Note("buildsPlaced"); // hidden vendor move-in milestone
                }
            });
        }

        /// <summary>
        /// Generalized ghost placement mode (homes AND structures). The ghost
        /// follows the cursor cell by cell showing green/red validity; left
        /// click on a valid cell fires <paramref name="onPlace"/> ONCE and
        /// exits; right click (or a missing camera/grid) cancels and fires
        /// <paramref name="onCancel"/> (refund hook). A null
        /// <paramref name="validate"/> uses the default placeability rule.
        /// </summary>
        public void BeginPlaceBuilding(Sprite ghostSprite, float scale,
            System.Func<Vector2Int, bool> validate,
            System.Action<Vector2Int, Vector3> onPlace,
            System.Action onCancel = null)
        {
            if (ghostSprite == null || onPlace == null)
            {
                onCancel?.Invoke();
                return;
            }

            _movingHome = null;
            _placing = true;
            _placeValidate = validate;
            _placeOnPlace = onPlace;
            _placeOnCancel = onCancel;

            var menu = SelectionMenuUI.Instance;
            if (menu != null && menu.IsOpen) menu.Close();

            // Spawn the ghost under the cursor so it never flashes at origin.
            Vector3 pos = Vector3.zero;
            var cam = Camera.main;
            var mouse = Mouse.current;
            if (cam != null && mouse != null)
            {
                Vector2 screenPos = mouse.position.ReadValue();
                pos = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
                pos.z = 0f;
            }

            StartGhost(ghostSprite, new Vector3(scale, scale, 1f), pos);
        }

        private void UpdatePlaceMode()
        {
            var grid = TerrainGrid.Instance;
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (!_placing || grid == null || mouse == null || cam == null)
            {
                CancelPlaceMode();
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                CancelPlaceMode(); // cancel (fires the refund hook, if any)
                return;
            }

            Vector2 screenPos = mouse.position.ReadValue();
            Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            worldPos.z = 0f;

            bool inBounds = grid.TryWorldToCell(worldPos, out Vector2Int cell);
            bool valid = inBounds
                && (_placeValidate != null ? _placeValidate(cell) : IsCellPlaceable(grid, cell));

            if (_ghost != null)
            {
                _ghost.transform.position = inBounds ? grid.CellCenterWorld(cell) : worldPos;
                _ghost.color = valid ? GhostValid : GhostInvalid;
            }

            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (UIInputLock.BlockDirectKeys) return;

            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem != null && eventSystem.IsPointerOverGameObject()) return;

            if (!valid) return;

            ConsumedClickFrame = Time.frameCount;
            var onPlace = _placeOnPlace;
            Vector3 center = grid.CellCenterWorld(cell);
            ExitPlaceMode(); // one placement per trip through the build menu
            onPlace?.Invoke(cell, center);
        }

        /// <summary>Cancel path: tears the mode down, THEN fires the refund hook.</summary>
        private void CancelPlaceMode()
        {
            var onCancel = _placeOnCancel;
            ExitPlaceMode();
            onCancel?.Invoke();
        }

        private void ExitPlaceMode()
        {
            _placing = false;
            _placeValidate = null;
            _placeOnPlace = null;
            _placeOnCancel = null;
            DestroyGhost();
        }

        private void UpdateMoveMode()
        {
            // Home destroyed mid-move, or scene systems missing: bail out.
            var grid = TerrainGrid.Instance;
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (_movingHome == null || grid == null || mouse == null || cam == null)
            {
                ExitMoveMode();
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                ExitMoveMode(); // cancel
                return;
            }

            Vector2 screenPos = mouse.position.ReadValue();
            Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            worldPos.z = 0f;

            bool inBounds = grid.TryWorldToCell(worldPos, out Vector2Int cell);
            bool valid = inBounds && IsCellPlaceable(grid, cell);

            if (_ghost != null)
            {
                _ghost.transform.position = inBounds ? grid.CellCenterWorld(cell) : worldPos;
                _ghost.color = valid ? GhostValid : GhostInvalid;
            }

            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (UIInputLock.BlockDirectKeys) return;

            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem != null && eventSystem.IsPointerOverGameObject()) return;

            if (!valid) return;

            ConsumedClickFrame = Time.frameCount;
            _movingHome.MoveTo(cell, grid.CellCenterWorld(cell));
            ExitMoveMode();
        }

        /// <summary>Default placement rule, exposed for the build menu's
        /// structure rows: usable land, not water, no plant, no home.</summary>
        public static bool IsPlaceableCell(Vector2Int cell) =>
            IsCellPlaceable(TerrainGrid.Instance, cell);

        private static bool IsCellPlaceable(TerrainGrid grid, Vector2Int cell)
        {
            if (grid == null || !grid.InBounds(cell)) return false;
            if (grid.GetSurface(cell) == Surface.Water) return false;
            if (PlantManager.Instance != null && PlantManager.Instance.HasPlantAt(cell)) return false;
            if (Home.AnyAtCell(cell)) return false;
            return true;
        }

        private void ExitMoveMode()
        {
            _movingHome = null;
            DestroyGhost();
        }

        /// <summary>Shared ghost setup for move + placement modes.</summary>
        private void StartGhost(Sprite sprite, Vector3 scale, Vector3 position)
        {
            DestroyGhost();

            var go = new GameObject("PlacementGhost");
            _ghost = go.AddComponent<SpriteRenderer>();
            _ghost.sortingOrder = 60;
            _ghost.color = GhostInvalid;
            if (sprite != null)
            {
                _ghost.sprite = sprite;
                go.transform.localScale = scale;
            }

            go.transform.position = position;
        }

        private void DestroyGhost()
        {
            if (_ghost == null) return;
            Destroy(_ghost.gameObject);
            _ghost = null;
        }
    }
}
