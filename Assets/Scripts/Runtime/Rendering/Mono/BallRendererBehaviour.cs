using Scripts.Runtime.Physics.Mono;
using Scripts.Runtime.Rendering.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace Scripts.Runtime.Rendering.Mono
{
    /// <summary>
    /// Draws every ball in one instanced call. Owns the GPU side only: the icosphere mesh, the
    /// positions buffer, and the draw parameters.
    ///
    /// Execution order 100 so this LateUpdate runs after the simulation's (order 0), where the job
    /// is completed.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class BallRendererBehaviour : MonoBehaviour
    {
        private static readonly int PositionsId = Shader.PropertyToID("_Positions");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");

        [SerializeField]
        private BallPhysicsSimulationBehaviour simulation;

        [SerializeField]
        [Tooltip("Material using the SdfBalls/BallInstanced shader.")]
        private Material material;

        [SerializeField]
        [Range(0, IcosphereMeshBuilder.MaxSubdivisions)]
        [Tooltip("Icosphere subdivisions. 1 = 80 triangles per ball. Triangle count scales by 4 per level.")]
        private int subdivisions = 1;

        [SerializeField]
        [Tooltip("Culling volume for the whole draw. Must contain every ball; a ball outside it is not drawn.")]
        private Bounds drawBounds = new Bounds(new Vector3(0f, 5f, 0f), new Vector3(30f, 30f, 30f));

        private Mesh _ballMesh;
        private GraphicsBuffer _positionsBuffer;
        private MaterialPropertyBlock _materialProperties;
        private RenderParams _renderParams;

        public int TrianglesPerBall => 20 << (2 * subdivisions);

        private void Start()
        {
            _ballMesh = IcosphereMeshBuilder.Build(subdivisions);
            _materialProperties = new MaterialPropertyBlock();

            _renderParams = new RenderParams(material)
            {
                matProps = _materialProperties,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
                layer = gameObject.layer,
            };
        }

        private void LateUpdate()
        {
            if (simulation == null || material == null)
            {
                return;
            }

            var positions = simulation.Positions;
            EnsureBuffer(positions.Length);

            _positionsBuffer.SetData(positions);
            _materialProperties.SetBuffer(PositionsId, _positionsBuffer);
            _materialProperties.SetFloat(RadiusId, simulation.Radius);

            _renderParams.worldBounds = drawBounds;
            Graphics.RenderMeshPrimitives(_renderParams, _ballMesh, submeshIndex: 0, instanceCount: positions.Length);
        }

        private void OnDestroy()
        {
            _positionsBuffer?.Dispose();
            _positionsBuffer = null;

            if (_ballMesh != null)
            {
                Destroy(_ballMesh);
            }
        }

        /// <summary>
        /// One structured buffer of float3, recreated only when the ball count changes. Stride 12 is
        /// fine for a structured buffer; the 16-byte rule applies to constant buffers only.
        /// </summary>
        private void EnsureBuffer(int count)
        {
            if (_positionsBuffer != null && _positionsBuffer.count == count)
            {
                return;
            }

            _positionsBuffer?.Dispose();
            _positionsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(float) * 3);
        }

        #region Gizmos
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(drawBounds.center, drawBounds.size);
        }

        #endregion
    }
}
