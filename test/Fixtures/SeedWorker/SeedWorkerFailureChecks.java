package io.github.richasy.rodel.seed;

import java.io.IOException;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletionException;
import java.util.concurrent.ExecutionException;

/**
 * Review: a sampler future wraps cancellation before the worker's terminal-response boundary. Recognizing only
 * the outer exception reports a false failure; matching message text or arbitrary causes hides genuine faults.
 * This fixture calls the production classifier with actual JDK wrappers. It does not claim a timed vanilla race.
 */
public final class SeedWorkerFailureChecks {
    public static void main(String[] args) {
        var cancelled = new CancellationException("normal viewport replacement");
        require(SeedWorker.isCancellation(cancelled), "Direct cancellation must remain cancellation.");
        require(SeedWorker.isCancellation(new CompletionException(new ExecutionException(cancelled))),
                "Nested sampler wrappers must produce a cancelled terminal response.");
        var failure = new IllegalStateException("CancellationException is only text here");
        require(!SeedWorker.isCancellation(failure), "Exception messages must not classify cancellation.");
        require(!SeedWorker.isCancellation(new CompletionException(new ExecutionException(failure))),
                "A genuine wrapped sampler failure must remain an error.");
        require(!SeedWorker.isCancellation(new CompletionException((Throwable) null)), "A missing cause is not cancellation.");
        require(!SeedWorker.isCancellation(new IOException("storage failure", cancelled)),
                "A genuine non-transport error must not be unwrapped into cancellation.");
        System.out.println("PASS SeedWorkerFailureChecks");
    }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
