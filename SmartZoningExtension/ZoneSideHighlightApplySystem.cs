using Game;
using Game.Common;
using Game.Net;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Colossal.Mathematics;

namespace ZoneDepth
{
    public struct ZoneSidePreview : IComponentData
    {
        public bool m_Left;
    }

    /// <summary>
    /// Applies the temporary side preview after the vanilla zone update pass.
    /// The game normally does this from ZoneCellHighlightJob; this late pass
    /// keeps the preview visible while the cursor is stationary as well.
    /// </summary>
    public partial class ZoneSideHighlightApplySystem : GameSystemBase
    {
        private EntityQuery m_Previews;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Previews = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ZoneSidePreview>(),
                    ComponentType.ReadOnly<Curve>(),
                    ComponentType.ReadOnly<SubBlock>()
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() }
            });
        }

        protected override void OnUpdate()
        {
            if (Mod.CurrentSetting == null || !Mod.CurrentSetting.Enabled)
                return;

            EntityManager entityManager = World.EntityManager;
            NativeArray<Entity> previews = m_Previews.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < previews.Length; i++)
            {
                Entity road = previews[i];
                ZoneSidePreview preview = entityManager.GetComponentData<ZoneSidePreview>(road);
                if (!entityManager.Exists(road) || !entityManager.HasComponent<Curve>(road) ||
                    !entityManager.HasBuffer<SubBlock>(road))
                    continue;

                Curve curve = entityManager.GetComponentData<Curve>(road);
                bool left = preview.m_Left;
                DynamicBuffer<SubBlock> subBlocks = entityManager.GetBuffer<SubBlock>(road, true);
                for (int j = 0; j < subBlocks.Length; j++)
                {
                    Entity blockEntity = subBlocks[j].m_SubBlock;
                    if (blockEntity == Entity.Null || !entityManager.Exists(blockEntity) ||
                        !entityManager.HasComponent<Block>(blockEntity) ||
                        !entityManager.HasBuffer<Cell>(blockEntity))
                        continue;

                    Block block = entityManager.GetComponentData<Block>(blockEntity);
                    if (IsLeftOfRoad(curve, block.m_Position) != left)
                        continue;

                    DynamicBuffer<Cell> cells = entityManager.GetBuffer<Cell>(blockEntity);
                    for (int k = 0; k < cells.Length; k++)
                    {
                        Cell cell = cells[k];
                        cell.m_State |= CellFlags.Highlight;
                        cells[k] = cell;
                    }

                    if (!entityManager.HasComponent<Updated>(blockEntity))
                        entityManager.AddComponentData(blockEntity, default(Updated));
                }
            }
            previews.Dispose();
        }

        private static bool IsLeftOfRoad(Curve curve, float3 position)
        {
            MathUtils.Distance(curve.m_Bezier.xz, position.xz, out float t);
            t = math.clamp(t, 0f, 1f);
            float u = 1f - t;
            float2 tangent =
                3f * u * u * (curve.m_Bezier.xz.b - curve.m_Bezier.xz.a) +
                6f * u * t * (curve.m_Bezier.xz.c - curve.m_Bezier.xz.b) +
                3f * t * t * (curve.m_Bezier.xz.d - curve.m_Bezier.xz.c);
            float2 point =
                u * u * u * curve.m_Bezier.xz.a +
                3f * u * u * t * curve.m_Bezier.xz.b +
                3f * u * t * t * curve.m_Bezier.xz.c +
                t * t * t * curve.m_Bezier.xz.d;
            float2 delta = position.xz - point;
            return tangent.x * delta.y - tangent.y * delta.x > 0f;
        }
    }
}
