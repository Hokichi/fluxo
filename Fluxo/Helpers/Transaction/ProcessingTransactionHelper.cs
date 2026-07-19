namespace Fluxo.Helpers.Transaction;

public static class ProcessingTransactionHelper
{
    public static int FindNextPendingIndex(IReadOnlyList<State> states, int currentIndex)
    {
        for (var index = currentIndex + 1; index < states.Count; index++)
        {
            if (states[index] == State.Pending)
                return index;
        }

        return -1;
    }

    public static int FindPreviousProcessedIndex(IReadOnlyList<State> states, int currentIndex)
    {
        for (var index = currentIndex - 1; index >= 0; index--)
        {
            if (states[index] == State.Processed)
                return index;
        }

        return -1;
    }

    public enum State { Pending, Processed, Skipped }
}
