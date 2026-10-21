import { useEffect, useState } from "react";
import type { Api, AttemptRow, ThresholdEntry } from "../api/client";
import { explain } from "../messages";
import { previewThreshold } from "../preview";

interface Props {
  api: Api;
  onDone: () => void;
}

const THRESHOLD_NAME = "voice";

export function Analyst({ api, onDone }: Props) {
  const [attempts, setAttempts] = useState<AttemptRow[]>([]);
  const [thresholds, setThresholds] = useState<ThresholdEntry[]>([]);
  const [value, setValue] = useState("0.8");
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    let cancelled = false;
    Promise.all([api.attempts(), api.thresholds()])
      .then(([rows, entries]) => {
        if (cancelled) return;
        setAttempts(rows);
        setThresholds(entries);
        const current = entries.find((entry) => entry.name === THRESHOLD_NAME);
        if (current) setValue(String(current.value));
      })
      .catch((failure: unknown) => {
        if (!cancelled) setError(explain(failure));
      });
    return () => {
      cancelled = true;
    };
  }, [api]);

  const parsed = Number(value);
  const valid = Number.isFinite(parsed) && parsed > 0 && parsed <= 1;
  const preview = valid ? previewThreshold(attempts, parsed) : null;

  async function save() {
    setError(null);
    setSaved(false);
    try {
      const entry = await api.setThreshold(THRESHOLD_NAME, parsed);
      setThresholds((all) => [entry, ...all.filter((other) => other.name !== entry.name)]);
      setSaved(true);
    } catch (failure) {
      setError(explain(failure));
    }
  }

  return (
    <section aria-labelledby="analyst-heading">
      <h1 id="analyst-heading">Analyst</h1>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      <h2>Threshold</h2>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          void save();
        }}
      >
        <label>
          Cosine score needed to pass (0 to 1)
          <input type="number" min="0" max="1" step="0.01" value={value} onChange={(e) => setValue(e.target.value)} />
        </label>
        {preview && (
          <p aria-live="polite">
            At {parsed.toFixed(2)}, {preview.wouldPass} of {preview.scored} scored voice attempts would pass (
            {preview.passingNow} passed at the thresholds in force when they happened).
          </p>
        )}
        <div className="actions">
          <button type="submit" disabled={!valid}>
            Save threshold
          </button>
        </div>
        {saved && <p role="status">Saved.</p>}
      </form>
      {thresholds.length > 0 && (
        <p>
          In force: {thresholds.map((entry) => `${entry.name} ${entry.value} (model ${entry.modelId})`).join(", ")}
        </p>
      )}

      <h2>Attempts</h2>
      <table>
        <caption>Most recent step-up attempts</caption>
        <thead>
          <tr>
            <th scope="col">Time</th>
            <th scope="col">User</th>
            <th scope="col">Mode</th>
            <th scope="col">Score</th>
            <th scope="col">Result</th>
            <th scope="col">Reason</th>
          </tr>
        </thead>
        <tbody>
          {attempts.map((attempt) => (
            <tr key={attempt.id}>
              <td>{new Date(attempt.at).toLocaleString()}</td>
              <td>{attempt.user}</td>
              <td>{attempt.mode}</td>
              <td>{attempt.score === null ? "–" : attempt.score.toFixed(3)}</td>
              <td>{attempt.result}</td>
              <td>{attempt.reason ?? "–"}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {attempts.length === 0 && <p>No attempts yet.</p>}
      <div className="actions">
        <button type="button" className="secondary" onClick={onDone}>
          Back
        </button>
      </div>
    </section>
  );
}
