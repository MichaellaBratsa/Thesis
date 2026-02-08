using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using TMPro;

public class SphereCounter : MonoBehaviour
{
    public TextMeshProUGUI uiText; // Σύρε εδώ το κείμενο
    public string targetTag = "Player"; // Βεβαιώσου ότι το χέρι έχει αυτό το Tag

    // Χρησιμοποιούμε Trigger για να μην "κλωτσάει" το χέρι
    private void OnTriggerEnter(Collider other)
    {
        // Ελέγχουμε αν αυτό που μπήκε στη σφαίρα είναι το Χέρι
        if (other.CompareTag(targetTag))
        {
            // 1. Εμφάνισε το μήνυμα
            if (uiText != null)
            {
                uiText.text = "Yes!";
            }

            // 2. Εξαφάνισε τη σφαίρα
            Destroy(gameObject);
        }
    }
}
