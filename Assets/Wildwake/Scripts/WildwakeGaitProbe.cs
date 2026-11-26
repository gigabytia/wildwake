using UnityEngine;

[DefaultExecutionOrder(-1000)]
public sealed class WildwakeGaitProbe : MonoBehaviour
{
    public readonly Vector2[] Gaps = new Vector2[2];
    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0) return;
        var animator = GetComponent<Animator>();
        float scale = transform.lossyScale.y;
        for (int index = 0; index < 2; index++)
        {
            var goal = index == 0 ? AvatarIKGoal.LeftFoot : AvatarIKGoal.RightFoot;
            float height = scale * (index == 0 ? animator.leftFeetBottomHeight : animator.rightFeetBottomHeight);
            var rotation = animator.GetIKRotation(goal);
            var root = animator.GetIKPosition(goal);
            Gaps[index] = new Vector2((root + rotation * (height * new Vector3(0f, -1f, -.42f))).y,
                (root + rotation * (height * new Vector3(0f, -1f, .88f))).y);
        }
    }
}
