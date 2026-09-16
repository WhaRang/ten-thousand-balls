using Scripts.Runtime.Physics.Mono;
using Scripts.Runtime.Rendering.Utils;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Scripts.Runtime.Rendering.Mono
{
    /// <summary>
    /// Draws every ball in one instanced call. Owns the GPU side only: the icosphere mesh, the
    /// positions buffer, and the draw parameters. Reads the simulation's positions after its job
    /// has completed and uploads them unchanged: 12 bytes per ball per frame is the whole upload.
    ///
    /// Execution order 100 so this LateUpdate runs after the simulation's (order 0), where the job
    /// is completed. Unity does not order LateUpdate across components on its own.
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

        private Mesh mesh;
        private GraphicsBuffer positionsBuffer;
        private MaterialPropertyBlock properties;
        private RenderParams renderParams;

        /// <summary>For the on-screen readout: 20 · 4ⁿ triangles per ball at n subdivisions.</summary>
        public int TrianglesPerBall => 20 << (2 * subdivisions);

        private void Start()
        {
            mesh = IcosphereMeshBuilder.Build(subdivisions);
            properties = new MaterialPropertyBlock();

            // The property block carries everything per-draw so the material asset is never mutated.
            renderParams = new RenderParams(material)
            {
                matProps = properties,
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

            // Safe to read here: the simulation completed its job in its own LateUpdate (order 0).
            NativeArray<float3> positions = simulation.Positions;
            EnsureBuffer(positions.Length);

            positionsBuffer.SetData(positions);
            properties.SetBuffer(PositionsId, positionsBuffer);
            properties.SetFloat(RadiusId, simulation.Radius);

            renderParams.worldBounds = drawBounds; // re-read each frame so it can be tuned live
            Graphics.RenderMeshPrimitives(renderParams, mesh, submeshIndex: 0, instanceCount: positions.Length);
        }

        private void OnDestroy()
        {
            positionsBuffer?.Dispose();
            positionsBuffer = null;

            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        /// <summary>
        /// One structured buffer of float3, recreated only when the ball count changes. Stride 12 is
        /// fine for a structured buffer; the 16-byte rule applies to constant buffers only.
        /// </summary>
        private void EnsureBuffer(int count)
        {
            if (positionsBuffer != null && positionsBuffer.count == count)
            {
                return;
            }

            positionsBuffer?.Dispose();
            positionsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(float) * 3);
        }

        private void OnDrawGizmosSelected()
        {
            // The culling volume, so a ball vanishing at the edge of it is diagnosable at a glance.
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(drawBounds.center, drawBounds.size);
        }
    }
}
