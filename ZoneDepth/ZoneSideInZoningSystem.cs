using System;
using System.Reflection;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem;

namespace ZoneDepth
{
    /// <summary>
    /// Makes the road-side zoning toggle usable while the vanilla ZoneTool is
    /// selected.  Left click disables the side nearest the road hit; right
    /// click enables it again.  The edit is made on the road's Upgraded flags,
    /// which is the same state consumed by the game's road/block systems.
    /// </summary>
    public partial class ZoneSideInZoningSystem : GameSystemBase
    {
        private struct CompetitionMatch
        {
            public Entity Road;
            public bool Left;
            public Entity SourceBlockEntity;
            public Block SourceBlock;
            public Entity CandidateBlockEntity;
            public Block CandidateBlock;
            public Quad2 SourceCorners;
            public Quad2 CandidateCorners;
            public float OverlapArea;
        }

        private struct PriorityRestoreEntry
        {
            public Entity Road;
            public bool Left;
        }

        private ToolSystem m_ToolSystem;
        private ZoneToolSystem m_ZoneToolSystem;
        private ToolOutputBarrier m_OutputBarrier;
        private FieldInfo m_ApplyZoneField;
        private FieldInfo m_RemoveZoneField;
        private MethodInfo m_GetRaycastResult;
        private FieldInfo m_ToolRaycastField;
        private ToolRaycastSystem m_ToolRaycastSystem;
        private EntityQuery m_RoadEdges;
        private Entity m_HoveredRoad;
        private Entity m_HoverPreviewRoad;
        private bool m_HoverPreviewLeft;
        private bool m_HoverPreviewActive;
        private bool m_LastZoneToolActive;
        private bool m_PreviousApplyPressed;
        private bool m_PreviousRemovePressed;
        private ProxyAction m_ApplyProxy;
        private ProxyAction m_RemoveProxy;
        private bool m_InputSubscribed;
        private ZoneSideWorldMarker m_WorldMarker;
        private readonly Dictionary<Entity, ZoneSideWorldMarker> m_CompetitionMarkers = new Dictionary<Entity, ZoneSideWorldMarker>();
        private int m_LastClickFrame = -1;
        private Entity m_LastClickRoad = Entity.Null;
        private bool m_LastClickDisable;
        private bool m_LastClickAlt;
        private readonly List<PriorityRestoreEntry> m_PriorityRestoreQueue = new List<PriorityRestoreEntry>();
        private int m_PriorityRestoreIndex;
        private int m_PriorityRestoreWaitFrames;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_ZoneToolSystem = World.GetOrCreateSystemManaged<ZoneToolSystem>();
            m_OutputBarrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_ToolRaycastField = typeof(ZoneToolSystem).GetField("m_ToolRaycastSystem", BindingFlags.Instance | BindingFlags.NonPublic);
            m_ToolRaycastSystem = m_ToolRaycastField?.GetValue(m_ZoneToolSystem) as ToolRaycastSystem;
            m_RoadEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Edge>(),
                    ComponentType.ReadOnly<Curve>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            m_ApplyZoneField = typeof(ZoneToolSystem).GetField("m_ApplyZone", flags);
            m_RemoveZoneField = typeof(ZoneToolSystem).GetField("m_RemoveZone", flags);
            m_GetRaycastResult = typeof(ZoneToolSystem).GetMethod(
                "GetRaycastResult",
                flags,
                null,
                new[] { typeof(Entity).MakeByRefType(), typeof(RaycastHit).MakeByRefType() },
                null);
            m_HoveredRoad = Entity.Null;
            m_HoverPreviewRoad = Entity.Null;
            m_HoverPreviewActive = false;
            m_WorldMarker = new ZoneSideWorldMarker();
        }

        protected override void OnDestroy()
        {
            if (m_InputSubscribed)
            {
                if (m_ApplyProxy != null)
                    m_ApplyProxy.onInteraction -= OnApplyInteraction;
                if (m_RemoveProxy != null)
                    m_RemoveProxy.onInteraction -= OnRemoveInteraction;
            }
            m_WorldMarker?.Dispose();
            foreach (ZoneSideWorldMarker marker in m_CompetitionMarkers.Values)
                marker.Dispose();
            m_CompetitionMarkers.Clear();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            AdvancePriorityRestore();
            if (Mod.CurrentSetting == null || !Mod.CurrentSetting.Enabled)
            {
                m_HoveredRoad = Entity.Null;
                ClearHoverPreview();
                return;
            }

            bool zoneToolActive = m_ToolSystem.activeTool == m_ZoneToolSystem;
            if (zoneToolActive != m_LastZoneToolActive)
            {
                m_LastZoneToolActive = zoneToolActive;
                if (!zoneToolActive)
                {
                    m_HoveredRoad = Entity.Null;
                    ClearHoverPreview();
                    m_PreviousApplyPressed = false;
                    m_PreviousRemovePressed = false;
                }
            }

            if (!zoneToolActive)
                return;
            EnsureInputSubscriptions();

            if (m_ToolRaycastSystem == null && m_ToolRaycastField != null)
                m_ToolRaycastSystem = m_ToolRaycastField.GetValue(m_ZoneToolSystem) as ToolRaycastSystem;

            if (m_ToolRaycastSystem != null)
            {
                TypeMask oldTypeMask = m_ToolRaycastSystem.typeMask;
                Layer oldLayerMask = m_ToolRaycastSystem.netLayerMask;
                m_ToolRaycastSystem.typeMask = oldTypeMask | TypeMask.Net;
                m_ToolRaycastSystem.netLayerMask = oldLayerMask | Layer.Road;
            }

            if (m_GetRaycastResult == null || m_ApplyZoneField == null || m_RemoveZoneField == null)
                return;

            bool hasRoad = TryGetRoadUnderCursor(out Entity hoveredRoad, out float3 hitPosition);
            m_HoveredRoad = hasRoad ? hoveredRoad : Entity.Null;
            bool mouseHeld = Mouse.current != null &&
                (Mouse.current.leftButton.isPressed ||
                 Mouse.current.rightButton.isPressed ||
                 Mouse.current.middleButton.isPressed ||
                 Mouse.current.forwardButton.isPressed ||
                 Mouse.current.backButton.isPressed);
            if (mouseHeld)
            {
                ClearHoverPreview();
                m_WorldMarker?.Hide();
                HideCompetitionMarkers();
            }
            else
            {
                UpdateHoverPreview(hasRoad, hoveredRoad, hitPosition);
            }
            // Read the physical mouse edge as well as tool actions. Paint
            // Bucket may disable/consume its Apply Zone action over terrain,
            // but the physical click is still unambiguous here.
            if (Mouse.current != null)
            {
                bool disable = Mouse.current.leftButton.wasPressedThisFrame;
                bool enable = Mouse.current.rightButton.wasPressedThisFrame;
                if ((disable || enable) && hasRoad)
                    ProcessDirectClick(disable);
                if (Mouse.current.middleButton.wasPressedThisFrame && hasRoad)
                {
                    EntityManager entityManager = World.EntityManager;
                    bool left = IsLeftOfRoad(entityManager.GetComponentData<Curve>(hoveredRoad), hitPosition);
                    BeginPriorityRestore(hoveredRoad, left);
                }
            }
            return;

        }

        private void EnsureInputSubscriptions()
        {
            if (m_InputSubscribed)
                return;
            if (InputManager.instance == null)
                return;
            ProxyActionMap toolMap = InputManager.instance.FindActionMap("Tool");
            if (toolMap == null)
                return;
            toolMap.actions.TryGetValue("Apply", out m_ApplyProxy);
            toolMap.actions.TryGetValue("Secondary Apply", out m_RemoveProxy);
            if (m_ApplyProxy == null || m_RemoveProxy == null)
                return;
            m_ApplyProxy.onInteraction += OnApplyInteraction;
            m_RemoveProxy.onInteraction += OnRemoveInteraction;
            m_InputSubscribed = true;
        }

        private void OnApplyInteraction(ProxyAction action, InputActionPhase phase)
        {
            if (phase == InputActionPhase.Started && m_ToolSystem.activeTool == m_ZoneToolSystem)
                ProcessDirectClick(disable: true);
        }

        private void OnRemoveInteraction(ProxyAction action, InputActionPhase phase)
        {
            if (phase == InputActionPhase.Started && m_ToolSystem.activeTool == m_ZoneToolSystem)
                ProcessDirectClick(disable: false);
        }

        private void ProcessDirectClick(bool disable)
        {
            if (Mod.CurrentSetting == null || !Mod.CurrentSetting.Enabled)
                return;
            if (!TryGetRoadUnderCursor(out Entity road, out float3 hitPosition))
                return;
            EntityManager entityManager = World.EntityManager;
            bool left = IsLeftOfRoad(entityManager.GetComponentData<Curve>(road), hitPosition);
            bool altPressed = Keyboard.current != null &&
                (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
            int frame = UnityEngine.Time.frameCount;
            if (m_LastClickFrame == frame && m_LastClickRoad == road &&
                m_LastClickDisable == disable && m_LastClickAlt == altPressed)
                return;
            m_LastClickFrame = frame;
            m_LastClickRoad = road;
            m_LastClickDisable = disable;
            m_LastClickAlt = altPressed;

            // Alt-click applies the same operation to every competing side.
            // When enabling, include sides that are currently disabled so
            // they can be found and restored as well.
            if (altPressed)
                SetCompetingRoadSidesDisabled(road, left, disable, includeDisabled: !disable);
            else
                SetRoadSideDisabled(road, left, disable);
        }

        internal void ProcessPreToolClick()
        {
            if (Mod.CurrentSetting == null || !Mod.CurrentSetting.Enabled ||
                m_ToolSystem.activeTool != m_ZoneToolSystem ||
                m_GetRaycastResult == null || m_ApplyZoneField == null || m_RemoveZoneField == null)
                return;

            IProxyAction apply = m_ApplyZoneField.GetValue(m_ZoneToolSystem) as IProxyAction;
            IProxyAction remove = m_RemoveZoneField.GetValue(m_ZoneToolSystem) as IProxyAction;
            if (apply == null || remove == null)
                return;

            bool applyPressed = apply.IsPressed();
            bool removePressed = remove.IsPressed();
            bool disable = apply.WasPressedThisFrame() || (applyPressed && !m_PreviousApplyPressed);
            bool enable = remove.WasPressedThisFrame() || (removePressed && !m_PreviousRemovePressed);
            m_PreviousApplyPressed = applyPressed;
            m_PreviousRemovePressed = removePressed;
            if (!disable && !enable)
                return;
            ProcessDirectClick(disable);
        }

        private void SetRoadSideDisabled(Entity road, bool left, bool disable)
        {
            EntityManager entityManager = World.EntityManager;
            if (road == Entity.Null || !entityManager.Exists(road))
                return;

            bool hadUpgraded = entityManager.HasComponent<Upgraded>(road);
            Upgraded upgraded = hadUpgraded ? entityManager.GetComponentData<Upgraded>(road) : default;
            CompositionFlags flags = upgraded.m_Flags;
            if (left)
                flags.m_Left = SetZonesDisabled(flags.m_Left, disable);
            else
                flags.m_Right = SetZonesDisabled(flags.m_Right, disable);
            Upgraded replacement = new Upgraded { m_Flags = flags };
            if (hadUpgraded)
                entityManager.SetComponentData(road, replacement);
            else
                entityManager.AddComponentData(road, replacement);
            RequestRoadRefresh(road);
        }

        private void SetCompetingRoadSidesDisabled(Entity sourceRoad, bool sourceLeft,
            bool disable, bool includeDisabled)
        {
            EntityManager entityManager = World.EntityManager;
            List<CompetitionMatch> matches = FindCompetitionMatches(sourceRoad, sourceLeft, includeDisabled);
            foreach (CompetitionMatch match in matches)
                SetRoadSideDisabled(match.Road, match.Left, disable);
        }

        private void BeginPriorityRestore(Entity sourceRoad, bool sourceLeft)
        {
            if (m_PriorityRestoreQueue.Count > 0)
                return;

            List<CompetitionMatch> matches = FindCompetitionMatches(sourceRoad, sourceLeft, includeDisabled: false);
            if (matches.Count == 0)
                return;

            // Recreate the least-overlapping competitors first so the blocks
            // that interfere most with the hovered side receive priority last.
            matches.Sort((a, b) => a.OverlapArea.CompareTo(b.OverlapArea));
            m_PriorityRestoreQueue.Clear();
            foreach (CompetitionMatch match in matches)
            {
                if (!IsSideDisabled(match.Road, match.Left))
                {
                    m_PriorityRestoreQueue.Add(new PriorityRestoreEntry
                    {
                        Road = match.Road,
                        Left = match.Left
                    });
                }
            }

            if (m_PriorityRestoreQueue.Count == 0)
                return;

            // First disable every competitor and let the game process that
            // state. Re-enable them one per update so each receives a fresh
            // build order in the requested weak-to-strong sequence.
            foreach (PriorityRestoreEntry entry in m_PriorityRestoreQueue)
                SetRoadSideDisabled(entry.Road, entry.Left, disable: true);
            m_PriorityRestoreIndex = 0;
            m_PriorityRestoreWaitFrames = 1;
        }

        private void AdvancePriorityRestore()
        {
            if (m_PriorityRestoreQueue.Count == 0)
                return;
            if (m_PriorityRestoreWaitFrames > 0)
            {
                m_PriorityRestoreWaitFrames--;
                return;
            }

            while (m_PriorityRestoreIndex < m_PriorityRestoreQueue.Count)
            {
                PriorityRestoreEntry entry = m_PriorityRestoreQueue[m_PriorityRestoreIndex++];
                if (World.EntityManager.Exists(entry.Road) && IsSideDisabled(entry.Road, entry.Left))
                {
                    SetRoadSideDisabled(entry.Road, entry.Left, disable: false);
                    return;
                }
            }

            m_PriorityRestoreQueue.Clear();
            m_PriorityRestoreIndex = 0;
        }

        private List<CompetitionMatch> FindCompetitionMatches(Entity sourceRoad, bool sourceLeft,
            bool includeDisabled)
        {
            List<CompetitionMatch> matches = new List<CompetitionMatch>();
            EntityManager entityManager = World.EntityManager;
            if (!entityManager.Exists(sourceRoad) || !entityManager.HasComponent<Curve>(sourceRoad))
                return matches;

            HashSet<Entity> sourceBlocks = CollectSideBlocks(sourceRoad,
                entityManager.GetComponentData<Curve>(sourceRoad), sourceLeft);
            if (sourceBlocks.Count == 0)
                return matches;
            Curve sourceCurve = entityManager.GetComponentData<Curve>(sourceRoad);

            NativeArray<Entity> roads = m_RoadEdges.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < roads.Length; i++)
                {
                    Entity candidateRoad = roads[i];
                    if (candidateRoad == sourceRoad || !entityManager.Exists(candidateRoad) ||
                        !entityManager.HasComponent<Curve>(candidateRoad) ||
                        !entityManager.HasBuffer<SubBlock>(candidateRoad))
                        continue;

                    Curve candidateCurve = entityManager.GetComponentData<Curve>(candidateRoad);
                    DynamicBuffer<SubBlock> candidateBlocks = entityManager.GetBuffer<SubBlock>(candidateRoad, true);
                    bool foundCandidate = false;
                    for (int candidateIndex = 0; candidateIndex < candidateBlocks.Length; candidateIndex++)
                    {
                        Entity candidateBlockEntity = candidateBlocks[candidateIndex].m_SubBlock;
                        if (!entityManager.Exists(candidateBlockEntity) ||
                            !entityManager.HasComponent<Block>(candidateBlockEntity))
                            continue;

                        Block candidateBlock = entityManager.GetComponentData<Block>(candidateBlockEntity);
                        bool candidateLeft = IsLeftOfRoad(candidateCurve, candidateBlock.m_Position);
                        bool candidateDisabled = IsSideDisabled(candidateRoad, candidateLeft);
                        if (candidateDisabled && !includeDisabled)
                            continue;

                        foreach (Entity sourceBlockEntity in sourceBlocks)
                        {
                            if (!entityManager.Exists(sourceBlockEntity) ||
                                !entityManager.HasComponent<Block>(sourceBlockEntity))
                                continue;

                            Block sourceBlock = entityManager.GetComponentData<Block>(sourceBlockEntity);
                            // A polygon overlap across either road does not
                            // mean the blocks can compete: the road separates
                            // the two zoning areas. Both blocks must lie on
                            // the same relevant side of each other's road.
                            if (IsLeftOfRoad(candidateCurve, sourceBlock.m_Position) != candidateLeft ||
                                IsLeftOfRoad(sourceCurve, candidateBlock.m_Position) != sourceLeft)
                                continue;

                            // The game treats aligned blocks that touch at
                            // their side edges as neighbors, not as competing
                            // zoning areas. Exclude this parallel-adjacency
                            // case before testing rectangular overlap.
                            if (ZoneUtils.IsNeighbor(sourceBlock, candidateBlock))
                                continue;

                            if (!BlocksOverlap(entityManager, sourceBlock, candidateBlockEntity, candidateBlock,
                                useFullCandidateBlock: candidateDisabled, out Quad2 sourceCorners,
                                out Quad2 candidateCorners))
                                continue;

                            matches.Add(new CompetitionMatch
                            {
                                Road = candidateRoad,
                                Left = candidateLeft,
                                SourceBlockEntity = sourceBlockEntity,
                                SourceBlock = sourceBlock,
                                CandidateBlockEntity = candidateBlockEntity,
                                CandidateBlock = candidateBlock,
                                SourceCorners = sourceCorners,
                                CandidateCorners = candidateCorners,
                                OverlapArea = GetIntersectionArea(sourceCorners, candidateCorners)
                            });
                            foundCandidate = true;
                            // Match at most one side per road, exactly as the
                            // preview shows it. This prevents a click from
                            // changing an unhighlighted opposite side.
                            break;
                        }

                        if (foundCandidate)
                            break;
                    }
                }
            }
            finally
            {
                roads.Dispose();
            }

            return matches;
        }

        private bool TryGetRoadUnderCursor(out Entity road, out float3 hitPosition)
        {
            road = Entity.Null;
            hitPosition = default;
            object[] args = { Entity.Null, default(RaycastHit) };
            bool raycastSucceeded = (bool)m_GetRaycastResult.Invoke(m_ZoneToolSystem, args);
            Entity result = (Entity)args[0];
            RaycastHit raycast = (RaycastHit)args[1];
            hitPosition = raycast.m_HitPosition;
            if (!raycastSucceeded)
                return false;

            EntityManager entityManager = World.EntityManager;
            if (result != Entity.Null && entityManager.Exists(result) && entityManager.HasComponent<Curve>(result))
                road = result;
            else if (raycast.m_HitEntity != Entity.Null && entityManager.Exists(raycast.m_HitEntity) && entityManager.HasComponent<Curve>(raycast.m_HitEntity))
                road = raycast.m_HitEntity;
            else
                road = FindRoadAtHitPosition(hitPosition);

            return road != Entity.Null;
        }

        private Entity FindRoadAtHitPosition(float3 hitPosition)
        {
            Entity nearestRoad = Entity.Null;
            float nearestDistance = float.MaxValue;
            NativeArray<Entity> roads = m_RoadEdges.ToEntityArray(Allocator.Temp);
            EntityManager entityManager = World.EntityManager;
            try
            {
                for (int i = 0; i < roads.Length; i++)
                {
                    Entity candidate = roads[i];
                    Curve curve = entityManager.GetComponentData<Curve>(candidate);
                    MathUtils.Distance(curve.m_Bezier.xz, hitPosition.xz, out float t);
                    t = math.clamp(t, 0f, 1f);
                    float2 closest = EvaluateCurve(curve, t);
                    float distance = math.distance(closest, hitPosition.xz);
                    if (distance >= nearestDistance)
                        continue;
                    GetRoadWidth(candidate, out float width, out _);
                    // The game reports terrain while the pointer is over the
                    // road in ZoneTool. Accept only hits inside the segment's
                    // actual half-width, with a small tolerance for raycast
                    // and curve approximation error.
                    if (distance > width * 0.5f + 0.25f)
                        continue;
                    nearestRoad = candidate;
                    nearestDistance = distance;
                }
            }
            finally
            {
                roads.Dispose();
            }
            return nearestRoad;
        }

        private HashSet<Entity> CollectSideBlocks(Entity road, Curve curve, bool left)
        {
            HashSet<Entity> blocksOnSide = new HashSet<Entity>();
            EntityManager entityManager = World.EntityManager;
            if (!entityManager.HasBuffer<SubBlock>(road))
                return blocksOnSide;
            DynamicBuffer<SubBlock> subBlocks = entityManager.GetBuffer<SubBlock>(road, true);
            for (int i = 0; i < subBlocks.Length; i++)
            {
                Entity blockEntity = subBlocks[i].m_SubBlock;
                if (blockEntity == Entity.Null || !entityManager.Exists(blockEntity) ||
                    !entityManager.HasComponent<Block>(blockEntity))
                    continue;
                Block block = entityManager.GetComponentData<Block>(blockEntity);
                if (IsLeftOfRoad(curve, block.m_Position) == left)
                    blocksOnSide.Add(blockEntity);
            }
            return blocksOnSide;
        }

        private bool IsSideDisabled(Entity road, bool left)
        {
            EntityManager entityManager = World.EntityManager;
            if (!entityManager.HasComponent<Upgraded>(road))
                return false;
            CompositionFlags.Side side = left
                ? entityManager.GetComponentData<Upgraded>(road).m_Flags.m_Left
                : entityManager.GetComponentData<Upgraded>(road).m_Flags.m_Right;
            return (side & CompositionFlags.Side.ZonesDisabled) != 0;
        }

        private static bool SameSet(HashSet<Entity> left, HashSet<Entity> right)
        {
            if (left.Count != right.Count)
                return false;
            foreach (Entity entity in left)
                if (!right.Contains(entity))
                    return false;
            return true;
        }

        private void SetZoneCellsHighlighted(EntityCommandBuffer ecb, Entity entity, bool value)
        {
            EntityManager entityManager = World.EntityManager;
            if (entity == Entity.Null || !entityManager.Exists(entity) ||
                !entityManager.HasBuffer<Cell>(entity) || !entityManager.HasComponent<Block>(entity))
                return;

            Block block = entityManager.GetComponentData<Block>(entity);
            ValidArea area = entityManager.HasComponent<ValidArea>(entity)
                ? entityManager.GetComponentData<ValidArea>(entity)
                : default;
            DynamicBuffer<Cell> cells = entityManager.GetBuffer<Cell>(entity);
            int width = math.max(0, block.m_Size.x);
            int height = math.max(0, block.m_Size.y);
            int firstX = math.clamp(area.m_Area.x, 0, width);
            int lastX = math.clamp(area.m_Area.y, firstX, width);
            int firstZ = math.clamp(area.m_Area.z, 0, height);
            int lastZ = math.clamp(area.m_Area.w, firstZ, height);
            for (int z = firstZ; z < lastZ; z++)
            {
                for (int x = firstX; x < lastX; x++)
                {
                    int index = z * width + x;
                    if (index < 0 || index >= cells.Length)
                        continue;
                    Cell cell = cells[index];
                    cell.m_State = value
                        ? cell.m_State | CellFlags.Highlight
                        : cell.m_State & ~CellFlags.Highlight;
                    cells[index] = cell;
                }
            }
            if (!entityManager.HasComponent<Updated>(entity))
                ecb.AddComponent(entity, default(Updated));
        }

        private void UpdateHoverPreview(bool hasRoad, Entity road, float3 hitPosition)
        {
            if (!hasRoad)
            {
                ClearHoverPreview();
                m_WorldMarker?.Hide();
                HideCompetitionMarkers();
                return;
            }

            Curve curve = World.EntityManager.GetComponentData<Curve>(road);
            bool left = IsLeftOfRoad(curve, hitPosition);
            bool disabled = IsSideDisabled(road, left);
            if (m_HoverPreviewRoad == road &&
                m_HoverPreviewLeft == left &&
                m_HoverPreviewActive == disabled)
                return;

            ClearHoverPreview();
            m_HoverPreviewRoad = road;
            m_HoverPreviewLeft = left;
            m_HoverPreviewActive = disabled;
            EntityManager entityManager = World.EntityManager;

            // When zoning exists on this side, keep the game's cell preview.
            if (!disabled)
            {
                m_WorldMarker?.Hide();
                if (entityManager.HasComponent<ZoneSidePreview>(road))
                    entityManager.SetComponentData(road, new ZoneSidePreview { m_Left = left });
                else
                    entityManager.AddComponentData(road, new ZoneSidePreview { m_Left = left });
                UpdateCompetitionMarkers(road, left);
                RequestRoadRefresh(road);
                return;
            }

            // When zoning is disabled on this side there are no cells to
            // highlight, so show the half-road marker instead.
            float roadWidth = 9f;
            float middleOffset = 0f;
            if (entityManager.HasComponent<Composition>(road))
            {
                Entity composition = entityManager.GetComponentData<Composition>(road).m_Edge;
                if (composition != Entity.Null && entityManager.Exists(composition) &&
                    entityManager.HasComponent<NetCompositionData>(composition))
                {
                    NetCompositionData data = entityManager.GetComponentData<NetCompositionData>(composition);
                    roadWidth = math.max(2f, data.m_Width);
                    middleOffset = data.m_MiddleOffset;
                }
            }
            float3 a = curve.m_Bezier.a;
            float3 b = curve.m_Bezier.b;
            float3 c = curve.m_Bezier.c;
            float3 d = curve.m_Bezier.d;
            m_WorldMarker?.Show(a, b, c, d, left, roadWidth, middleOffset, new UnityEngine.Color(0.5f, 0.5f, 1f, 0.28f));
            HideCompetitionMarkers();
            RequestRoadRefresh(road);
        }

        private void ClearHoverPreview()
        {
            Entity oldRoad = m_HoverPreviewRoad;
            EntityManager entityManager = World.EntityManager;
            if (oldRoad != Entity.Null && entityManager.Exists(oldRoad) &&
                entityManager.HasComponent<ZoneSidePreview>(oldRoad))
                entityManager.RemoveComponent<ZoneSidePreview>(oldRoad);
            RequestRoadRefresh(oldRoad);
            m_HoverPreviewRoad = Entity.Null;
            m_HoverPreviewActive = false;
            m_WorldMarker?.Hide();
            HideCompetitionMarkers();
        }

        private void UpdateCompetitionMarkers(Entity sourceRoad, bool sourceLeft)
        {
            EntityManager entityManager = World.EntityManager;
            HashSet<Entity> desired = new HashSet<Entity>();
            List<CompetitionMatch> matches = FindCompetitionMatches(sourceRoad, sourceLeft, includeDisabled: false);
            foreach (CompetitionMatch match in matches)
            {
                Entity candidate = match.Road;
                if (!entityManager.Exists(candidate) || !entityManager.HasComponent<Curve>(candidate))
                    continue;
                desired.Add(candidate);
                if (!m_CompetitionMarkers.TryGetValue(candidate, out ZoneSideWorldMarker marker))
                {
                    marker = new ZoneSideWorldMarker();
                    m_CompetitionMarkers[candidate] = marker;
                }
                GetRoadWidth(candidate, out float width, out float middleOffset);
                Bezier4x3 bezier = entityManager.GetComponentData<Curve>(candidate).m_Bezier;
                marker.Show(bezier.a, bezier.b, bezier.c, bezier.d, match.Left, width, middleOffset,
                    new UnityEngine.Color(1f, 0.2f, 0.2f, 0.28f));
            }

            List<Entity> stale = new List<Entity>();
            foreach (Entity candidate in m_CompetitionMarkers.Keys)
                if (!desired.Contains(candidate)) stale.Add(candidate);
            foreach (Entity candidate in stale)
            {
                m_CompetitionMarkers[candidate].Dispose();
                m_CompetitionMarkers.Remove(candidate);
            }
        }

        private void HideCompetitionMarkers()
        {
            foreach (ZoneSideWorldMarker marker in m_CompetitionMarkers.Values)
                marker.Hide();
        }

        private bool HasSideBlocks(Entity road, bool left)
        {
            EntityManager entityManager = World.EntityManager;
            if (!entityManager.HasBuffer<SubBlock>(road)) return false;
            Curve curve = entityManager.GetComponentData<Curve>(road);
            DynamicBuffer<SubBlock> blocks = entityManager.GetBuffer<SubBlock>(road, true);
            for (int i = 0; i < blocks.Length; i++)
            {
                Entity block = blocks[i].m_SubBlock;
                if (block != Entity.Null && entityManager.Exists(block) && entityManager.HasComponent<Block>(block) &&
                    IsLeftOfRoad(curve, entityManager.GetComponentData<Block>(block).m_Position) == left)
                    return true;
            }
            return false;
        }

        private static bool BlocksOverlap(EntityManager entityManager, Block source, Entity candidateEntity, Block candidate,
            out Quad2 sourceCorners, out Quad2 candidateCorners)
        {
            return BlocksOverlap(entityManager, source, candidateEntity, candidate,
                useFullCandidateBlock: false, out sourceCorners, out candidateCorners);
        }

        private static bool BlocksOverlap(EntityManager entityManager, Block source, Entity candidateEntity, Block candidate,
            bool useFullCandidateBlock, out Quad2 sourceCorners, out Quad2 candidateCorners)
        {
            sourceCorners = default;
            candidateCorners = default;
            sourceCorners = ZoneUtils.CalculateCorners(source);
            if (useFullCandidateBlock)
            {
                candidateCorners = ZoneUtils.CalculateCorners(candidate);
            }
            else
            {
                if (!entityManager.HasComponent<ValidArea>(candidateEntity))
                    return false;
                ValidArea validArea = entityManager.GetComponentData<ValidArea>(candidateEntity);
                if (validArea.m_Area.y <= validArea.m_Area.x || validArea.m_Area.w <= validArea.m_Area.z)
                    return false;
                candidateCorners = ZoneUtils.CalculateCorners(candidate, validArea);
            }
            sourceCorners = MathUtils.Expand(sourceCorners, -0.01f);
            candidateCorners = MathUtils.Expand(candidateCorners, -0.01f);
            return MathUtils.Intersect(sourceCorners, candidateCorners);
        }

        private static float GetIntersectionArea(Quad2 subject, Quad2 clip)
        {
            List<float2> polygon = new List<float2> { subject.a, subject.b, subject.c, subject.d };
            float orientation = SignedArea(new List<float2> { clip.a, clip.b, clip.c, clip.d }) >= 0f ? 1f : -1f;
            float2[] clipPoints = { clip.a, clip.b, clip.c, clip.d };

            for (int edge = 0; edge < clipPoints.Length && polygon.Count > 0; edge++)
            {
                float2 edgeStart = clipPoints[edge];
                float2 edgeEnd = clipPoints[(edge + 1) % clipPoints.Length];
                List<float2> input = polygon;
                polygon = new List<float2>();
                float2 previous = input[input.Count - 1];
                float previousDistance = orientation * Cross(edgeEnd - edgeStart, previous - edgeStart);
                bool previousInside = previousDistance >= -0.0001f;

                for (int i = 0; i < input.Count; i++)
                {
                    float2 current = input[i];
                    float currentDistance = orientation * Cross(edgeEnd - edgeStart, current - edgeStart);
                    bool currentInside = currentDistance >= -0.0001f;
                    if (currentInside != previousInside)
                    {
                        float denominator = previousDistance - currentDistance;
                        if (math.abs(denominator) > 0.000001f)
                        {
                            float t = previousDistance / denominator;
                            polygon.Add(previous + (current - previous) * t);
                        }
                    }
                    if (currentInside)
                        polygon.Add(current);
                    previous = current;
                    previousDistance = currentDistance;
                    previousInside = currentInside;
                }
            }

            return polygon.Count >= 3 ? math.abs(SignedArea(polygon)) : 0f;
        }

        private static float SignedArea(List<float2> polygon)
        {
            float twiceArea = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                float2 current = polygon[i];
                float2 next = polygon[(i + 1) % polygon.Count];
                twiceArea += current.x * next.y - current.y * next.x;
            }
            return twiceArea * 0.5f;
        }

        private static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private void GetRoadWidth(Entity road, out float width, out float middleOffset)
        {
            width = 9f;
            middleOffset = 0f;
            EntityManager entityManager = World.EntityManager;
            if (!entityManager.HasComponent<Composition>(road)) return;
            Entity composition = entityManager.GetComponentData<Composition>(road).m_Edge;
            if (composition == Entity.Null || !entityManager.Exists(composition) ||
                !entityManager.HasComponent<NetCompositionData>(composition)) return;
            NetCompositionData data = entityManager.GetComponentData<NetCompositionData>(composition);
            width = math.max(2f, data.m_Width);
            middleOffset = data.m_MiddleOffset;
        }

        private void RequestRoadRefresh(Entity road)
        {
            EntityManager entityManager = World.EntityManager;
            if (road == Entity.Null || !entityManager.Exists(road))
                return;

            ToggleUpdated(road);
            if (!entityManager.HasBuffer<SubBlock>(road))
                return;
            DynamicBuffer<SubBlock> subBlocks = entityManager.GetBuffer<SubBlock>(road, true);
            for (int i = 0; i < subBlocks.Length; i++)
            {
                Entity block = subBlocks[i].m_SubBlock;
                if (block != Entity.Null && entityManager.Exists(block))
                    ToggleUpdated(block);
            }
        }

        private void ToggleUpdated(Entity entity)
        {
            EntityManager entityManager = World.EntityManager;
            if (entityManager.HasComponent<Updated>(entity))
                entityManager.RemoveComponent<Updated>(entity);
            entityManager.AddComponentData(entity, default(Updated));
        }

        private static float2 EvaluateCurve(Curve curve, float t)
        {
            float u = 1f - t;
            return u * u * u * curve.m_Bezier.xz.a +
                3f * u * u * t * curve.m_Bezier.xz.b +
                3f * u * t * t * curve.m_Bezier.xz.c +
                t * t * t * curve.m_Bezier.xz.d;
        }

        private static CompositionFlags.Side SetZonesDisabled(CompositionFlags.Side side, bool disabled)
        {
            return disabled
                ? side | CompositionFlags.Side.ZonesDisabled
                : side & ~CompositionFlags.Side.ZonesDisabled;
        }

        private static bool IsLeftOfRoad(Curve curve, float3 hitPosition)
        {
            float t;
            MathUtils.Distance(curve.m_Bezier.xz, hitPosition.xz, out t);
            t = math.clamp(t, 0f, 1f);
            float u = 1f - t;
            float2 tangent =
                3f * u * u * (curve.m_Bezier.xz.b - curve.m_Bezier.xz.a) +
                6f * u * t * (curve.m_Bezier.xz.c - curve.m_Bezier.xz.b) +
                3f * t * t * (curve.m_Bezier.xz.d - curve.m_Bezier.xz.c);
            float2 point = EvaluateCurve(curve, t);
            float2 delta = hitPosition.xz - point;
            return tangent.x * delta.y - tangent.y * delta.x > 0f;
        }
    }
}
