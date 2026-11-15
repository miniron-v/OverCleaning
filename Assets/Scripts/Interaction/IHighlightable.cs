namespace OverCleaning.Interaction
{
    /// <summary>
    /// 상호작용 대상으로 잡혔을 때 겉모습을 밝혀 알리는 물건.
    /// 켜고 끄는 것은 각자 기기의 PlayerInteractor가 한다 — 누구의 대상인지는
    /// 보는 사람마다 다르므로 동기화하지 않는다.
    /// </summary>
    public interface IHighlightable
    {
        /// <summary>지금 내 상호작용 대상인지. 참이면 밝아지고 거짓이면 제 색으로 돌아온다.</summary>
        void SetHighlighted(bool highlighted);
    }
}
