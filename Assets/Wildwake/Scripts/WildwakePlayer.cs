using UnityEngine;
using UnityEngine.InputSystem;

namespace Wildwake
{
    [RequireComponent(typeof(Animator))]
    public sealed class WildwakePlayer : MonoBehaviour
    {
        private const float GroundContactSpeed = -2f;
        private const float JumpBufferDuration = 0.12f;
        private const float CoyoteDuration = 0.08f;
        public const float SlideDuration = 0.62f;
        private const string AttackLayerName = "Upper Body Attack";

        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int ArmedParameter = Animator.StringToHash("Armed");
        private static readonly int GroundedParameter = Animator.StringToHash("Grounded");
        private static readonly int VerticalSpeedParameter = Animator.StringToHash("VerticalSpeed");
        private static readonly int EquipAction = Animator.StringToHash("Equip");
        private static readonly int AttackAction = Animator.StringToHash("Attack");
        private static readonly int DeathAction = Animator.StringToHash("Death");
        private static readonly int UnarmedLocomotion = Animator.StringToHash("Unarmed Locomotion");
        private static readonly int ArmedLocomotion = Animator.StringToHash("Armed Locomotion");

        [Header("Scene References")]
        [SerializeField] private CharacterController controller;
        [SerializeField] private Transform viewCamera;
        [SerializeField] private Transform groundWeapon;
        [SerializeField] private LayerMask groundLayers = ~0;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 3f;
        [SerializeField, Min(0.1f)] private float runSpeed = 6f;
        [SerializeField, Min(0.1f)] private float acceleration = 28f;
        [SerializeField, Min(0.1f)] private float turnSpeed = 1000f;
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.15f;
        [SerializeField, Min(0.1f)] private float airJumpHeight = 1.05f;
        [SerializeField, Min(0.1f)] private float jumpGravity = 24f;

        [Header("Combat")]
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField, Min(1f)] private float maxStamina = 100f;
        [SerializeField, Min(0.1f)] private float pickupRange = 1.8f;
        [SerializeField, Min(0.1f)] private float meleeRange = 2.1f;
        [SerializeField, Min(0f)] private float meleeDamage = 24f;
        [SerializeField, Min(0f)] private float meleeStaminaDamage = 24f;
        [SerializeField, Min(0f)] private float attackCost = 18f;
        [SerializeField, Min(0f)] private float staminaRecoveryPerSecond = 24f;
        [SerializeField, Min(0f)] private float staminaRecoveryDelay = 0.65f;

        private readonly Collider[] meleeTargets = new Collider[16];
        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private Animator animator;
        private int attackLayer;
        private Transform rightHand;
        private Transform rightIndex;
        private Transform rightMiddle;
        private Transform rightLittle;
        private Vector3 planarVelocity;
        private Vector2 previousMoveInput;
        private Vector3 slideDirection;
        private float slideRemaining;
        private float turnAnimationAt;
        private bool wasRunning;
        private bool airJumpUsed;
        private float verticalVelocity;
        private bool grounded;
        private float lastGroundContact = float.NegativeInfinity;
        private float jumpRequestedUntil = float.NegativeInfinity;
        private float health;
        private float stamina;
        private float staminaRecoveryAt;
        private float actionRequestUntil;
        private bool pendingPickup;
        private bool pendingMelee;

        public Transform Body => controller != null ? controller.transform : transform;
        public float HealthNormalized => health / maxHealth;
        public float StaminaNormalized => stamina / maxStamina;
        public bool IsDead => health <= 0f;
        public bool HasWeapon { get; private set; }

        public bool CanPickUpWeapon => !IsDead && !HasWeapon && groundWeapon != null && rightHand != null
            && grounded && !ActionLocked && (groundWeapon.position - Body.position).sqrMagnitude <= pickupRange * pickupRange;

        private bool ActionLocked => Time.time < actionRequestUntil || IsInAction(EquipAction) || IsInAction(AttackAction);

        private void Awake()
        {
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            attackLayer = animator.GetLayerIndex(AttackLayerName);
            if (attackLayer >= 0)
                animator.SetLayerWeight(attackLayer, 0f);
            if (controller == null)
            {
                controller = GetComponentInParent<CharacterController>();
            }

            if (viewCamera == null && Camera.main != null)
            {
                viewCamera = Camera.main.transform;
            }

            health = maxHealth;
            stamina = maxStamina;
            grounded = controller != null && controller.isGrounded;
            CacheHandBones();
        }

        private void Update()
        {
            if (IsDead || controller == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (!ActionLocked)
            {
                pendingPickup = false;
                pendingMelee = false;
                if (keyboard != null && keyboard.fKey.wasPressedThisFrame)
                {
                    TryPickupWeapon();
                }

                if (Cursor.lockState == CursorLockMode.Locked && Mouse.current != null
                    && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    TryAttack();
                }
            }

            UpdateMovement(keyboard);
            UpdateAttackLayer();
            if (Time.time >= staminaRecoveryAt)
            {
                stamina = Mathf.MoveTowards(stamina, maxStamina, staminaRecoveryPerSecond * Time.deltaTime);
            }
        }

        public bool TryPickupWeapon()
        {
            if (!CanPickUpWeapon)
            {
                return false;
            }

            pendingPickup = true;
            actionRequestUntil = Time.time + 0.25f;
            animator.SetTrigger(EquipAction);
            return true;
        }

        private void UpdateAttackLayer()
        {
            if (attackLayer < 0)
                return;

            // The empty state must have zero weight so it cannot override the locomotion pose.
            bool currentAttack = animator.GetCurrentAnimatorStateInfo(attackLayer).shortNameHash == AttackAction;
            float weight = currentAttack ? 1f : pendingMelee ? .001f : 0f;
            if (animator.IsInTransition(attackLayer))
            {
                bool nextAttack = animator.GetNextAnimatorStateInfo(attackLayer).shortNameHash == AttackAction;
                float progress = Mathf.Clamp01(animator.GetAnimatorTransitionInfo(attackLayer).normalizedTime);
                if (currentAttack != nextAttack)
                    weight = nextAttack ? progress : 1f - progress;
            }
            animator.SetLayerWeight(attackLayer, weight);
        }

        public bool TryAttack()
        {
            if (IsDead || !HasWeapon || ActionLocked || stamina < attackCost)
            {
                return false;
            }

            SpendStamina(attackCost);
            pendingMelee = true;
            actionRequestUntil = Time.time + 0.25f;
            animator.SetTrigger(AttackAction);
            return true;
        }

        public void ReceiveHit(float damage)
        {
            if (IsDead || !(damage > 0f))
            {
                return;
            }

            float damageTaken = Mathf.Min(damage, health);
            health -= damageTaken;
            WildwakeHud.ShowDamage(Body.position + Vector3.up * 1.55f, damageTaken, true);
            if (IsDead)
            {
                pendingPickup = false;
                pendingMelee = false;
                animator.ResetTrigger(EquipAction);
                animator.ResetTrigger(AttackAction);
                if (attackLayer >= 0)
                {
                    animator.SetLayerWeight(attackLayer, 0f);
                }

                animator.SetTrigger(DeathAction);
            }
        }

        // Animation event on the pickup clip.
        public void AttachWeapon()
        {
            if (!pendingPickup || IsDead || HasWeapon || groundWeapon == null || rightHand == null)
            {
                return;
            }

            pendingPickup = false;
            Vector3 palmDirection = rightMiddle != null
                ? (rightMiddle.position - rightHand.position).normalized
                : rightHand.right;
            Vector3 gripAxis = rightIndex != null && rightLittle != null
                ? (rightIndex.position - rightLittle.position).normalized
                : rightHand.up;
            Vector3 bladeFace = Vector3.Cross(gripAxis, palmDirection).normalized;
            if (bladeFace.sqrMagnitude < 0.01f)
            {
                bladeFace = rightHand.forward;
                gripAxis = rightHand.up;
            }

            float bladeLength = Mathf.Abs(groundWeapon.lossyScale.y);
            Vector3 grip = rightHand.position + palmDirection * 0.045f;
            Vector3 bladeCenter = grip + gripAxis * Mathf.Max(0f, bladeLength * 0.5f - 0.14f);
            foreach (Collider weaponCollider in groundWeapon.GetComponentsInChildren<Collider>())
            {
                weaponCollider.enabled = false;
            }

            groundWeapon.SetPositionAndRotation(bladeCenter, Quaternion.LookRotation(bladeFace, gripAxis));
            groundWeapon.SetParent(rightHand, true);
            HasWeapon = true;
            animator.SetBool(ArmedParameter, true);
        }

        // Animation event on the sword's impact frame.
        public void ApplyMeleeHit()
        {
            if (!pendingMelee || IsDead)
            {
                return;
            }

            pendingMelee = false;
            Vector3 origin = Body.position + Vector3.up * 0.9f;
            int count = Physics.OverlapSphereNonAlloc(origin, meleeRange, meleeTargets, ~0, QueryTriggerInteraction.Collide);
            WildwakeEnemy closest = null;
            float closestDistance = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                Collider candidate = meleeTargets[index];
                WildwakeEnemy enemy = candidate.GetComponentInParent<WildwakeEnemy>();
                if (enemy == null && !candidate.transform.IsChildOf(Body))
                {
                    enemy = candidate.GetComponentInChildren<WildwakeEnemy>();
                }

                if (enemy == null || enemy.IsDead)
                {
                    continue;
                }

                Vector3 offset = Vector3.ProjectOnPlane(enemy.transform.position - Body.position, Vector3.up);
                float distance = offset.sqrMagnitude;
                if (distance < closestDistance && Vector3.Dot(Body.forward, offset.normalized) >= 0.27f)
                {
                    closest = enemy;
                    closestDistance = distance;
                }
            }

            if (closest != null)
            {
                closest.ReceiveHit(meleeDamage, meleeStaminaDamage);
            }
        }

        private void UpdateMovement(Keyboard keyboard)
        {
            bool equipping = (pendingPickup && Time.time < actionRequestUntil) || IsInAction(EquipAction);
            Vector2 input = !equipping && keyboard != null ? ReadMovementKeys(keyboard) : Vector2.zero;
            Vector3 forward = viewCamera != null
                ? Vector3.ProjectOnPlane(viewCamera.forward, Vector3.up).normalized
                : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 direction = forward * input.y + right * input.x;
            bool hasDirection = direction.sqrMagnitude > 0.01f;
            bool running = hasDirection && keyboard != null &&
                (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (!equipping && grounded && slideRemaining <= 0f && keyboard != null && keyboard.cKey.wasPressedThisFrame)
            {
                slideDirection = hasDirection ? direction.normalized : Body.forward;
                slideRemaining = SlideDuration;
                jumpRequestedUntil = float.NegativeInfinity;
                PlayBaseMotion("Slide", .06f);
            }

            bool sliding = slideRemaining > 0f;
            if (!equipping && !sliding && grounded && wasRunning)
            {
                if (!hasDirection && planarVelocity.magnitude > walkSpeed * .48f)
                    PlayBaseMotion("Run to Stop", .075f);
                else if (running && planarVelocity.magnitude > walkSpeed && Time.time >= turnAnimationAt &&
                    PressedMovementDirection(keyboard, input))
                {
                    // A return to held input or a camera rotation should not start a turn clip.
                    float angle = -Vector2.SignedAngle(previousMoveInput, input);
                    if (Mathf.Abs(angle) >= 28f)
                    {
                        string state = Mathf.Abs(angle) >= 130f ? "Change Direction"
                            : angle < 0f ? "Run Left Turn" : "Run Right Turn";
                        PlayBaseMotion(state, .075f);
                        turnAnimationAt = Time.time + .16f;
                    }
                }
            }

            if (sliding)
            {
                planarVelocity = slideDirection * Mathf.Lerp(2f, runSpeed * 1.26f, slideRemaining / SlideDuration);
                direction = slideDirection;
                slideRemaining = Mathf.Max(0f, slideRemaining - Time.deltaTime);
            }
            else
            {
                Vector3 targetVelocity = direction * (running ? runSpeed : walkSpeed);
                planarVelocity = equipping ? Vector3.zero :
                    Vector3.MoveTowards(planarVelocity, targetVelocity, acceleration * Time.deltaTime);
            }

            if (sliding || hasDirection)
                Body.rotation = Quaternion.RotateTowards(Body.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);

            if (!equipping && !sliding && (!airJumpUsed || grounded) &&
                keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                jumpRequestedUntil = Time.time + JumpBufferDuration;
            }

            MoveCharacter(!equipping && !sliding);
            if (!grounded)
                slideRemaining = 0f;
            wasRunning = running && !sliding;
            previousMoveInput = input;

            float speed = new Vector2(controller.velocity.x, controller.velocity.z).magnitude;
            float blend = speed <= walkSpeed
                ? Mathf.InverseLerp(0f, walkSpeed, speed) * 0.5f
                : 0.5f + Mathf.InverseLerp(walkSpeed, runSpeed, speed) * 0.5f;
            animator.SetFloat(SpeedParameter, blend, 0.06f, Time.deltaTime);
        }

        private void MoveCharacter(bool canJump)
        {
            float now = Time.time;
            float deltaTime = Time.deltaTime;
            bool wantsJump = canJump && now <= jumpRequestedUntil;
            bool groundJump = wantsJump && now - lastGroundContact <= CoyoteDuration;
            bool airJump = wantsJump && !groundJump && !grounded && !airJumpUsed;
            bool jumping = groundJump || airJump;
            if (jumping)
            {
                verticalVelocity = Mathf.Sqrt(2f * jumpGravity * (airJump ? airJumpHeight : jumpHeight));
                jumpRequestedUntil = float.NegativeInfinity;
                lastGroundContact = float.NegativeInfinity;
                airJumpUsed = airJump;
                PlayBaseMotion(airJump ? "Forward Flip" : HasWeapon ? "Armed Jump" : "Unarmed Jump", .045f);
            }
            else if (grounded)
            {
                verticalVelocity = GroundContactSpeed;
            }

            float verticalDisplacement = verticalVelocity * deltaTime;
            if (jumping || !grounded)
            {
                verticalVelocity -= jumpGravity * deltaTime;
                verticalDisplacement -= 0.5f * jumpGravity * deltaTime * deltaTime;
            }

            CollisionFlags collisions = controller.Move(planarVelocity * deltaTime + Vector3.up * verticalDisplacement);
            if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            {
                verticalVelocity = 0f;
            }

            grounded = (collisions & CollisionFlags.Below) != 0 && verticalVelocity <= 0f;
            if (grounded)
            {
                lastGroundContact = now;
                verticalVelocity = GroundContactSpeed;
                airJumpUsed = false;
            }

            animator.SetBool(GroundedParameter, grounded);
            animator.SetFloat(VerticalSpeedParameter, grounded ? 0f : verticalVelocity);
        }

        private void PlayBaseMotion(string state, float blend)
        {
            animator.CrossFadeInFixedTime(Animator.StringToHash("Base Layer." + state), blend, 0);
        }

        private static Vector2 ReadMovementKeys(Keyboard keyboard)
        {
            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private static bool PressedMovementDirection(Keyboard keyboard, Vector2 input)
        {
            return (input.x < 0f && keyboard.aKey.wasPressedThisFrame) ||
                (input.x > 0f && keyboard.dKey.wasPressedThisFrame) ||
                (input.y < 0f && keyboard.sKey.wasPressedThisFrame) ||
                (input.y > 0f && keyboard.wKey.wasPressedThisFrame);
        }

        private bool IsInAction(int action)
        {
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layer);
                if (current.shortNameHash == action || current.tagHash == action)
                {
                    return true;
                }

                if (animator.IsInTransition(layer))
                {
                    AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(layer);
                    if (next.shortNameHash == action || next.tagHash == action)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void SpendStamina(float amount)
        {
            stamina = Mathf.Max(0f, stamina - amount);
            staminaRecoveryAt = Time.time + staminaRecoveryDelay;
        }

        private void CacheHandBones()
        {
            if (animator.isHuman)
            {
                rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                rightIndex = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
                rightMiddle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                rightLittle = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
                return;
            }

            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (bone.name.EndsWith("RightHand", System.StringComparison.OrdinalIgnoreCase))
                {
                    rightHand = bone;
                    break;
                }
            }
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || !animator.isHuman)
            {
                return;
            }

            bool allowContact = !IsDead && grounded && slideRemaining <= 0f;
            int state = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            float idleContact = state == UnarmedLocomotion || state == ArmedLocomotion
                ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.015f, .18f, animator.GetFloat(SpeedParameter)))
                : 0f;
            float scale = Mathf.Abs(transform.lossyScale.y);
            SolveFoot(AvatarIKGoal.LeftFoot, animator.leftFeetBottomHeight * scale, allowContact, idleContact);
            SolveFoot(AvatarIKGoal.RightFoot, animator.rightFeetBottomHeight * scale, allowContact, idleContact);
        }

        private void SolveFoot(AvatarIKGoal foot, float soleHeight, bool allowContact, float idleContact)
        {
            if (!allowContact)
            {
                animator.SetIKPositionWeight(foot, 0f);
                animator.SetIKRotationWeight(foot, 0f);
                return;
            }

            Vector3 animatedPosition = animator.GetIKPosition(foot);
            Quaternion animatedRotation = animator.GetIKRotation(foot);
            Vector3 soleOffset = Vector3.down * soleHeight;
            Vector3 animatedSole = animatedPosition + animatedRotation * soleOffset;
            int count = Physics.RaycastNonAlloc(animatedSole + Vector3.up * .35f, Vector3.down, groundHits, .8f,
                groundLayers, QueryTriggerInteraction.Ignore);
            RaycastHit ground = default;
            float closest = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = groundHits[index];
                if (hit.distance < closest && hit.normal.y > 0.55f && !hit.transform.IsChildOf(Body))
                {
                    ground = hit;
                    closest = hit.distance;
                }
            }

            if (ground.collider == null)
            {
                animator.SetIKPositionWeight(foot, 0f);
                animator.SetIKRotationWeight(foot, 0f);
                return;
            }

            float clearance = animatedSole.y - ground.point.y;
            float stepContact = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.018f, .13f, clearance));
            float weight = Mathf.Max(stepContact, idleContact);
            Vector3 heading = Vector3.ProjectOnPlane(animatedRotation * Vector3.forward, ground.normal);
            if (heading.sqrMagnitude < .001f)
                heading = Vector3.ProjectOnPlane(Body.forward, ground.normal);
            Quaternion aligned = Quaternion.LookRotation(heading, ground.normal);
            float rotationWeight = Mathf.Lerp(.6f * stepContact, 1f, idleContact);
            Vector3 contactOffset = Quaternion.Slerp(animatedRotation, aligned, rotationWeight) * soleOffset;
            float correction = Mathf.Clamp(ground.point.y + .007f - animatedPosition.y - contactOffset.y, -.12f, .12f);
            animator.SetIKPosition(foot, animatedPosition + Vector3.up * correction);
            animator.SetIKRotation(foot, aligned);
            animator.SetIKPositionWeight(foot, weight);
            animator.SetIKRotationWeight(foot, rotationWeight);
        }
    }
}
