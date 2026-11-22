using UnityEngine;

namespace DemonsWithin
{
    [RequireComponent(typeof(Animator))]
    public sealed class FighterRootMotion : MonoBehaviour
    {
        private Animator animator;
        private FighterPresentation presentation;

        public void Bind(FighterPresentation source)
        {
            animator = GetComponent<Animator>();
            presentation = source;
        }

        private void OnAnimatorMove()
        {
            if (presentation != null)
                presentation.ApplyRootMotion(animator.deltaPosition, animator.deltaRotation);
        }
    }
}
