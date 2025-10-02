using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Αυτό το script πρέπει να υπάρχει σε ένα GameObject στη σκηνή μας.
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();

    // Singleton pattern για να είναι εύκολα προσβάσιμο από παντού
    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            // Αν δεν υπάρχει, προσπάθησε να το βρεις στη σκηνή
            _instance = FindObjectOfType<UnityMainThreadDispatcher>();
            if (_instance == null)
            {
                // Αν πάλι δεν υπάρχει, δημιούργησέ το δυναμικά
                GameObject obj = new GameObject("UnityMainThreadDispatcher");
                _instance = obj.AddComponent<UnityMainThreadDispatcher>();
            }
        }
        return _instance;
    }

    void Awake()
    {
        // Εξασφαλίζουμε ότι δεν θα καταστραφεί όταν αλλάζουμε σκηνές
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }

    public void Update()
    {
        // Κάθε frame, εκτελεί ό,τι εντολές υπάρχουν στην ουρά
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    // Η μέθοδος που καλούμε από άλλα scripts για να βάλουμε μια εντολή στην ουρά
    public void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
}