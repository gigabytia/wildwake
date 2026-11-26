using UnityEngine;

namespace Wildwake
{
    [RequireComponent(typeof(Animator))]
    public sealed class WildwakeEnemy : MonoBehaviour
    {
        private enum Attack
        {
            None,
            Single,
            Double,
            Heavy
        }

        private static readonly int Moving = Animator.StringToHash("Moving");
        private static readonly int Stun = Animator.StringToHash("Stun");
        private static readonly int Die = Animator.StringToHash("Die");
        private static readonly int AttackTag = Animator.StringToHash("Attack");
        private static readonly int[] AttackTriggers =
        {
            Animator.StringToHash("AttackSingle"),
            Animator.StringToHash("AttackDouble"),
            Animator.StringToHash("AttackHeavy")
        };

        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private WildwakePlayer player;
        [SerializeField] private Transform shotOrigin;
        [SerializeField] private Transform secondShotOrigin;
        [SerializeField] private Transform heavyShotOrigin;
        [SerializeField] private Material projectileMaterial;

        [Header("Combat")]
        [SerializeField, Min(1f)] private float maxHealth = 110f;
        [SerializeField, Min(40f)] private float maxStamina = 80f;
        [SerializeField, Min(0f)] private float staminaRecovery = 8f;
        [SerializeField, Min(0.2f)] private float stunDuration = 2.5f;
        [SerializeField, Min(2f)] private float attackRange = 9f;
        [SerializeField, Min(0f)] private float approachSpeed = 2.1f;

        private CharacterController movementController;
        private CharacterController playerController;
        private Transform body;
        private float health;
        private float stamina;
        private float stunUntil;
        private float nextAttackAt;
        private float attackDeadline;
        private int attackIndex;
        private int requestedShots;
        private int releasedShots;
        private bool enteredAttackState;
        private bool finishRequested;
        private Attack activeAttack;

        public float HealthNormalized => Mathf.Clamp01(health / maxHealth);
        public float StaminaNormalized => Mathf.Clamp01(stamina / maxStamina);
        public bool IsStunned => !IsDead && stunUntil > Time.time;
        public bool IsDead => health <= 0f;

        public void Configure(Animator sourceAnimator, WildwakePlayer target, Transform firstOrigin,
            Material shotMaterial, Transform secondOrigin = null, Transform heavyOrigin = null)
        {
            animator = sourceAnimator;
            player = target;
            shotOrigin = firstOrigin;
            projectileMaterial = shotMaterial;
            secondShotOrigin = secondOrigin;
            heavyShotOrigin = heavyOrigin;
        }

        private void Awake()
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            // An off-camera enemy still needs its animation events to fire.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            movementController = GetComponent<CharacterController>();
            body = transform;
            health = maxHealth;
            stamina = maxStamina;
            nextAttackAt = Time.time + 1.4f;
        }

        private void Start()
        {
            if (player != null && player.Body != null)
                playerController = player.Body.GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (IsDead || player == null || player.IsDead)
            {
                animator.SetBool(Moving, false);
                return;
            }

            float now = Time.time;
            if (stunUntil > now)
                return;

            if (stunUntil > 0f)
            {
                stunUntil = 0f;
                stamina = maxStamina * 0.5f;
            }

            Vector3 toPlayer = player.Body.position - body.position;
            toPlayer.y = 0f;
            float playerDistance = toPlayer.magnitude;
            if (playerDistance > 0.01f)
            {
                float turnSpeed = activeAttack == Attack.None ? 360f : 165f;
                Quaternion facing = Quaternion.LookRotation(toPlayer, Vector3.up);
                body.rotation = Quaternion.RotateTowards(body.rotation, facing, turnSpeed * Time.deltaTime);
            }

            if (activeAttack != Attack.None)
            {
                bool playingAttack = animator.GetCurrentAnimatorStateInfo(0).tagHash == AttackTag ||
                    (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).tagHash == AttackTag);
                enteredAttackState |= playingAttack;
                if ((enteredAttackState && !playingAttack) || now >= attackDeadline)
                    finishRequested = true;
                return;
            }

            stamina = Mathf.MoveTowards(stamina, maxStamina, staminaRecovery * Time.deltaTime);
            bool approaching = playerDistance > attackRange;
            animator.SetBool(Moving, approaching);
            if (approaching)
            {
                float step = Mathf.Min(approachSpeed * Time.deltaTime, playerDistance - attackRange);
                Vector3 movement = toPlayer / playerDistance * step;
                if (movementController != null)
                    movementController.Move(movement + Vector3.down * (2f * Time.deltaTime));
                else
                    body.position += movement;
                return;
            }

            if (now >= nextAttackAt)
                BeginAttack();
        }

        private void BeginAttack()
        {
            activeAttack = (Attack)(attackIndex + 1);
            float staminaCost = activeAttack switch
            {
                Attack.Double => 27f,
                Attack.Heavy => 42f,
                _ => 18f
            };
            stamina = Mathf.Max(0f, stamina - staminaCost);
            requestedShots = 0;
            releasedShots = 0;
            enteredAttackState = false;
            finishRequested = false;
            attackDeadline = Time.time + 9f;
            animator.SetTrigger(AttackTriggers[attackIndex]);
            attackIndex = (attackIndex + 1) % AttackTriggers.Length;
        }

        public void ReleaseProjectile(int heavy)
        {
            if (activeAttack == Attack.None || IsDead || IsStunned || player == null || player.IsDead)
                return;

            int limit = activeAttack == Attack.Double ? 2 : 1;
            bool matchingSize = (heavy == 1) == (activeAttack == Attack.Heavy);
            if (matchingSize && requestedShots < limit)
                requestedShots++;
        }

        public void FinishAttack()
        {
            if (activeAttack != Attack.None)
                finishRequested = true;
        }

        private void LateUpdate()
        {
            // Sample the shard transforms once the Animator has written the release pose.
            while (releasedShots < requestedShots)
            {
                Fire(activeAttack, releasedShots);
                releasedShots++;
            }

            if (!finishRequested)
                return;

            float recovery = activeAttack == Attack.Heavy ? 1.9f : 1.35f;
            CancelAttack();
            nextAttackAt = Time.time + recovery + Random.Range(0f, 0.45f);
            if (stamina <= 0f && !IsDead)
                BeginStun();
        }

        private void Fire(Attack attack, int shotIndex)
        {
            if (player == null || player.IsDead)
                return;

            bool heavy = attack == Attack.Heavy;
            Transform origin = heavy && heavyShotOrigin != null ? heavyShotOrigin : shotOrigin;
            if (attack == Attack.Double && shotIndex == 1 && secondShotOrigin != null)
                origin = secondShotOrigin;

            Vector3 position = origin != null ? origin.position : body.position + Vector3.up * 1.05f;
            Vector3 target = playerController != null ? playerController.bounds.center :
                player.Body.position + Vector3.up * 0.98f;
            Vector3 direction = target - position;
            if (direction.sqrMagnitude < 0.0001f)
                direction = body.forward;
            direction.Normalize();

            float damage = attack switch
            {
                Attack.Double => 9f,
                Attack.Heavy => 28f,
                _ => 13f
            };
            WildwakeProjectile.Launch(position, direction, heavy, damage, body, player, projectileMaterial);
        }

        public void ReceiveHit(float damage, float staminaDamage)
        {
            if (IsDead)
                return;

            float applied = Mathf.Min(health, Mathf.Max(0f, damage));
            health -= applied;
            if (applied > 0f)
                WildwakeHud.ShowDamage(body.position + Vector3.up * 1.42f, applied, false);

            if (IsDead)
            {
                CancelAttack();
                animator.ResetTrigger(Stun);
                animator.SetBool(Moving, false);
                animator.SetTrigger(Die);
                foreach (Collider collider in GetComponents<Collider>())
                    collider.enabled = false;
                return;
            }

            if (IsStunned || staminaDamage <= 0f)
                return;

            stamina = Mathf.Max(0f, stamina - staminaDamage);
            if (stamina <= 0f)
                BeginStun();
        }

        private void BeginStun()
        {
            CancelAttack();
            stunUntil = Time.time + stunDuration;
            nextAttackAt = stunUntil + 0.8f;
            animator.SetBool(Moving, false);
            animator.SetTrigger(Stun);
        }

        private void CancelAttack()
        {
            if (activeAttack != Attack.None)
                animator.ResetTrigger(AttackTriggers[(int)activeAttack - 1]);

            activeAttack = Attack.None;
            finishRequested = false;
            requestedShots = 0;
            releasedShots = 0;
        }
    }
}
