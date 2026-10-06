using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 카메라와 자기 캐릭터 사이를 가리는 건물을 반투명하게 그린다. 보는 사람마다 따로 정한다.
    /// 건물 묶음의 부모에 붙이며, 자식 Renderer 하나를 건물 하나로 본다.
    /// 반투명 재질 하나를 함께 쓰고, 건물마다 원래 색만 덧입힌다.
    /// </summary>
    public sealed class BuildingCulling : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("캐릭터 발에서 이만큼 올라간 곳이 가려지는지 본다.")]
        [Min(0f)] [SerializeField] private float _targetHeight = 1f;

        [Tooltip("가릴 때 쓸 반투명 재질. Surface Type이 Transparent여야 한다.")]
        [SerializeField] private Material _fadedMaterial;

        [Range(0f, 1f)] [SerializeField] private float _fadedAlpha = 0.3f;

        private Renderer[] _buildings;
        private Material[] _originalMaterials;
        private bool[] _isFaded;
        private MaterialPropertyBlock _fadedProperties;

        private void Awake()
        {
            _buildings = GetComponentsInChildren<Renderer>(true);
            _originalMaterials = new Material[_buildings.Length];
            _isFaded = new bool[_buildings.Length];
            for (int index = 0; index < _buildings.Length; index++)
                _originalMaterials[index] = _buildings[index].sharedMaterial;
            _fadedProperties = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            Camera mainCamera = Camera.main;
            if (player == null || mainCamera == null)
                return;

            Vector3 origin = mainCamera.transform.position;
            Vector3 toTarget = player.transform.position + Vector3.up * _targetHeight - origin;
            Ray ray = new Ray(origin, toTarget);
            float targetDistance = toTarget.magnitude;

            for (int index = 0; index < _buildings.Length; index++)
            {
                bool isBlocking = _buildings[index].bounds.IntersectRay(ray, out float distance) && distance < targetDistance;
                SetFaded(index, isBlocking);
            }
        }

        private void SetFaded(int index, bool isFaded)
        {
            if (_isFaded[index] == isFaded)
                return;
            _isFaded[index] = isFaded;

            Renderer building = _buildings[index];
            if (!isFaded)
            {
                building.sharedMaterial = _originalMaterials[index];
                building.SetPropertyBlock(null);
                return;
            }

            Color color = _originalMaterials[index].GetColor(BaseColorId);
            color.a = _fadedAlpha;
            _fadedProperties.SetColor(BaseColorId, color);
            building.sharedMaterial = _fadedMaterial;
            building.SetPropertyBlock(_fadedProperties);
        }
    }
}
