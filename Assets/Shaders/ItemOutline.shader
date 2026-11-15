// 들 수 있는 물건의 테두리. 물건 메시를 조금 키운 사본에 입혀,
// 앞면을 걷어내고(Cull Front) 뒷면만 그리면 실루엣 바깥 테두리만 남는다.
// LightMode 태그가 없는 패스는 URP가 SRPDefaultUnlit으로 그려 준다.
Shader "OverCleaning/ItemOutline"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 0.9, 0.35, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Pass
        {
            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _BaseColor;

            float4 vert(float4 position : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(position);
            }

            fixed4 frag() : SV_Target
            {
                return _BaseColor;
            }
            ENDCG
        }
    }
}
