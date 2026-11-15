using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OverCleaning.Interaction
{
    /// <summary>
    /// 물건의 테두리를 밝혀 상호작용 대상임을 알리는 도우미.
    /// 몸통 메시마다 조금 키운 사본을 자식으로 만들어 외곽선 재질(앞면 걷어내기)로
    /// 그린다 — 물건 실루엣을 따라 테두리만 보인다. 보는 사람 기기만의 연출이다.
    /// 사본은 처음 켤 때 한 번 만들고, 그 뒤로는 켜고 끄기만 한다.
    /// </summary>
    public sealed class BodyHighlighter
    {
        /// <summary>외곽선 재질. 빌드에 실리도록 Resources에 두고 한 번만 읽는다.</summary>
        private const string MaterialPath = "ItemOutline";

        /// <summary>테두리 두께. 메시를 이 배율로 키운 사본이 실루엣 밖으로 삐져나온 폭이다.</summary>
        private const float OutlineScale = 1.06f;

        private static Material _outlineMaterial;
        private static bool _materialSearched;

        private readonly Component _root;
        private readonly List<GameObject> _outlines = new List<GameObject>();
        private bool _built;

        public BodyHighlighter(Component root)
        {
            _root = root;
        }

        public void SetHighlighted(bool highlighted)
        {
            if (highlighted && !_built)
                Build();

            foreach (GameObject outline in _outlines)
            {
                if (outline != null)
                    outline.SetActive(highlighted);
            }
        }

        private void Build()
        {
            _built = true;
            Material material = LoadMaterial();
            if (material == null)
                return;

            foreach (MeshFilter meshFilter in _root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh == null ||
                    meshFilter.GetComponent<MeshRenderer>() == null)
                    continue;

                var outline = new GameObject("Highlight Outline");
                outline.transform.SetParent(meshFilter.transform, false);
                outline.transform.localScale = Vector3.one * OutlineScale;
                outline.AddComponent<MeshFilter>().sharedMesh = meshFilter.sharedMesh;
                var outlineRenderer = outline.AddComponent<MeshRenderer>();
                outlineRenderer.sharedMaterial = material;
                outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
                outline.SetActive(false);
                _outlines.Add(outline);
            }
        }

        private static Material LoadMaterial()
        {
            if (!_materialSearched)
            {
                _materialSearched = true;
                _outlineMaterial = Resources.Load<Material>(MaterialPath);
                if (_outlineMaterial == null)
                    Debug.LogError($"Resources/{MaterialPath} 재질이 없어 테두리를 그릴 수 없습니다.");
            }

            return _outlineMaterial;
        }
    }
}
