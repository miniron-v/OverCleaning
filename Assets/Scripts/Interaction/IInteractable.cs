namespace OverCleaning.Interaction
{
    /// <summary>
    /// 플레이어가 가까이 가서 상호작용 키로 쓸 수 있는 대상.
    /// 콜라이더가 있는 오브젝트나 그 부모에 붙인다.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>지금 상호작용할 수 있는지. 아니면 안내도 띄우지 않는다.</summary>
        bool CanInteract { get; }

        /// <summary>가까이 갔을 때 보여줄 행동 이름. 예: "맵 선택".</summary>
        string Prompt { get; }

        /// <summary>자기 캐릭터를 조작하는 클라이언트에서만 불린다.</summary>
        void Interact();
    }
}
