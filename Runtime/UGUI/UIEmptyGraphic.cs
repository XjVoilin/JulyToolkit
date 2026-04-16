using UnityEngine;
using UnityEngine.UI;

namespace JulyToolkit
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIEmptyGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
        }
    }
}
