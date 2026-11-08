using Unity.Mathematics;
using UnityEngine;

namespace ZoneDepth
{
    /// <summary>
    /// A small temporary world-space marker for the side selected by the zone tool.
    /// It is deliberately separate from the road entity, so it cannot mark the
    /// whole segment or interfere with the game's network highlighting.
    /// </summary>
    internal sealed class ZoneSideWorldMarker
    {
        private readonly GameObject m_Root;
        private readonly LineRenderer m_SideLine;
        private readonly LineRenderer m_ArrowLine;
        private readonly Material m_Material;
        private readonly Texture2D m_EdgeTexture;

        public ZoneSideWorldMarker()
        {
            m_Root = new GameObject("ZoneDepth.SideMarker");
            m_Root.hideFlags = HideFlags.HideAndDontSave;
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            m_Material = new Material(shader);
            m_Material.hideFlags = HideFlags.HideAndDontSave;
            m_EdgeTexture = CreateSoftEdgeTexture();
            m_Material.mainTexture = m_EdgeTexture;
            m_SideLine = CreateLine("Side", 0.28f);
            m_ArrowLine = CreateLine("Arrow", 0.42f);
            Hide();
        }

        private LineRenderer CreateLine(string name, float width)
        {
            GameObject child = new GameObject(name);
            child.hideFlags = HideFlags.HideAndDontSave;
            child.transform.SetParent(m_Root.transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.material = m_Material;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static Texture2D CreateSoftEdgeTexture()
        {
            const int height = 32;
            Texture2D texture = new Texture2D(2, height, TextureFormat.RGBA32, false, true)
            {
                name = "ZoneDepth.SoftEdge",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < height; y++)
            {
                float v = y / (float)(height - 1);
                float edge = math.smoothstep(0f, 0.18f, v) * math.smoothstep(0f, 0.18f, 1f - v);
                Color pixel = new Color(1f, 1f, 1f, edge);
                texture.SetPixel(0, y, pixel);
                texture.SetPixel(1, y, pixel);
            }
            texture.Apply(false, true);
            return texture;
        }

        public void Show(float3 a, float3 b, float3 c, float3 d, bool left, float roadWidth, float middleOffset, Color color)
        {
            m_SideLine.startColor = color;
            m_SideLine.endColor = color;
            m_ArrowLine.startColor = color;
            m_ArrowLine.endColor = color;
            float3 p0 = a;
            float3 p1 = b;
            float3 p2 = c;
            float3 p3 = d;
            float2 tangent = math.normalizesafe((p3 - p0).xz, new float2(1f, 0f));
            float sideSign = left ? 1f : -1f;
            float2 baseNormal = new float2(-tangent.y, tangent.x);
            float2 normal = baseNormal * sideSign;
            float halfWidth = math.max(1f, roadWidth * 0.5f);
            float bandWidth = halfWidth;
            float3 offset = new float3(baseNormal.x, 0f, baseNormal.y) * middleOffset +
                new float3(normal.x, 0f, normal.y) * (halfWidth * 0.5f);
            p0 += offset; p1 += offset; p2 += offset; p3 += offset;
            p0.y += 0.35f; p1.y += 0.35f; p2.y += 0.35f; p3.y += 0.35f;

            m_SideLine.positionCount = 4;
            m_SideLine.widthMultiplier = bandWidth;
            m_SideLine.SetPosition(0, p0);
            m_SideLine.SetPosition(1, p1);
            m_SideLine.SetPosition(2, p2);
            m_SideLine.SetPosition(3, p3);

            float3 mid = math.lerp(p1, p2, 0.5f);
            float3 outward = new float3(normal.x, 0f, normal.y) * sideSign;
            float3 back = mid - math.normalizesafe(new float3(p2.x - p1.x, 0f, p2.z - p1.z), new float3(1f, 0f, 0f)) * 2.2f;
            float3 tip = mid + outward * 1.8f;
            m_ArrowLine.positionCount = 3;
            m_ArrowLine.SetPosition(0, back + outward * 1.5f);
            m_ArrowLine.SetPosition(1, tip);
            m_ArrowLine.SetPosition(2, back - outward * 1.5f);
            m_SideLine.enabled = true;
            m_ArrowLine.enabled = true;
        }

        public void Hide()
        {
            if (m_SideLine != null) m_SideLine.enabled = false;
            if (m_ArrowLine != null) m_ArrowLine.enabled = false;
        }

        public void Dispose()
        {
            if (m_Root != null)
                Object.Destroy(m_Root);
            if (m_Material != null)
                Object.Destroy(m_Material);
            if (m_EdgeTexture != null)
                Object.Destroy(m_EdgeTexture);
        }
    }
}
