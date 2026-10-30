using System.Collections.Generic;
using UnityEngine;

namespace RealisticCraterEdge
{
    internal sealed class GeyserPush : MonoBehaviour
    {
        private const float Lift = 14, Radius = 8;

        private Geyser geyser;
        private readonly HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();

        private void Awake()
        {
            geyser = GetComponent<Geyser>();
        }

        private void FixedUpdate()
        {
            pushed.Clear();
        }

        private void OnTriggerStay(Collider other)
        {
            if (geyser == null || !geyser.erupting)
            {
                return;
            }
            Rigidbody body = other.attachedRigidbody;
            if (body == null || body.isKinematic || !pushed.Add(body))
            {
                return;
            }
            Vector3 offset = body.position - transform.position;
            offset.y = 0;
            float centre = 1 - Mathf.Clamp01(offset.magnitude / Radius);
            body.AddForce(transform.up * (Lift * (0.4f + 0.6f * centre)), ForceMode.Acceleration);
        }
    }
}
