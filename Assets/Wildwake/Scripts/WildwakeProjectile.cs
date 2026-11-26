using UnityEngine;
using UnityEngine.Rendering;

namespace Wildwake
{
    public sealed class WildwakeProjectile : MonoBehaviour
    {
        private readonly Collider[] overlaps = new Collider[12];
        private readonly RaycastHit[] hits = new RaycastHit[12];
        private Transform owner;
        private WildwakePlayer target;
        private Vector3 direction;
        private float damage;
        private float speed;
        private float radius;
        private float expiresAt;

        public static void Launch(Vector3 position, Vector3 direction, bool heavy, float damage,
            Transform owner, WildwakePlayer target, Material material)
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = heavy ? "Spirit Projectile - Heavy" : "Spirit Projectile";
            orb.transform.SetPositionAndRotation(position + direction * (heavy ? 0.37f : 0.15f),
                Quaternion.LookRotation(direction));
            orb.transform.localScale = heavy ? Vector3.one * 1.06f : new Vector3(0.3f, 0.3f, 0.78f);

            Collider primitiveCollider = orb.GetComponent<Collider>();
            primitiveCollider.enabled = false;
            Destroy(primitiveCollider);

            MeshRenderer renderer = orb.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            WildwakeProjectile projectile = orb.AddComponent<WildwakeProjectile>();
            projectile.owner = owner;
            projectile.target = target;
            projectile.direction = direction;
            projectile.damage = damage;
            projectile.speed = heavy ? 8.6f : 12.5f;
            projectile.radius = heavy ? 0.53f : 0.15f;
            projectile.expiresAt = Time.time + 4f;
        }

        private void Update()
        {
            if (Time.time >= expiresAt)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 position = transform.position;
            float distance = speed * Time.deltaTime;
            int overlapCount = Physics.OverlapSphereNonAlloc(position, radius, overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlapCount; i++)
            {
                Collider overlap = overlaps[i];
                if (IsOwner(overlap))
                    continue;

                Impact(overlap);
                return;
            }

            // A sweep keeps a fast projectile from skipping the player or thin cover between frames.
            int count = Physics.SphereCastNonAlloc(position, radius, direction, hits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Collider nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.distance >= nearestDistance || IsOwner(hit.collider))
                    continue;

                nearest = hit.collider;
                nearestDistance = hit.distance;
            }

            if (nearest != null)
            {
                Impact(nearest);
                return;
            }

            transform.position = position + direction * distance;
        }

        private bool IsOwner(Collider collider) => owner != null && collider.transform.IsChildOf(owner);

        private void Impact(Collider collider)
        {
            if (target != null && !target.IsDead && collider.transform.IsChildOf(target.Body))
                target.ReceiveHit(damage);

            Destroy(gameObject);
        }
    }
}
