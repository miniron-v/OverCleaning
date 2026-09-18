using UnityEngine;

namespace OverCleaning.Network
{
    /// <summary>
    /// 인게임 진행을 맡는다. 지금은 게임을 끝내고 룸으로 돌아가는 기능만 둔다.
    /// 종료 조건(제한시간, 먼지 소진 등)은 정해지면 EndGame을 부르도록 연결한다.
    /// </summary>
    public sealed class GameScreen : MonoBehaviour
    {
        /// <summary>
        /// 게임을 끝내고 전원을 룸으로 돌려보낸다. 호스트만 실제로 씬을 바꾼다.
        /// 키 배치는 룸에서 다시 스폰될 때 기본값으로 돌아간다.
        /// </summary>
        public void EndGame()
        {
            GameSession.EnterRoom();
        }
    }
}
