# Test speech fixtures

Eight short WAV files (16 kHz mono PCM16). They are **synthetic speech**, not recordings of any person.

- Made with sherpa-onnx offline text-to-speech and the Piper `en_US-libritts_r-medium` voice model (CC BY 4.0, 904 voices), voices 50, 162 and 148, resampled from 22.05 kHz to 16 kHz.
- `voice1-*` is voice 50, `voice2-*` is voice 162, `voice3-*` is voice 148. The phrases are the ones the verifier tests use.
- The text-to-speech differs on every run, so each clip was generated until the real verifier (Whisper base.en) heard the right phrase, and the set was kept only if voice 50 and voice 162 separate by a cosine margin of at least 0.2.
- Regenerate with `scripts/fetch-models.sh --test` and then `STEPUP_GENERATE_FIXTURES=1 dotnet test tests/StepUp.Verifier.Tests -- --filter-class "*FixtureGenerator"`.
- They exist so the Category=Model tests are repeatable. They are not a dataset: they say nothing about accuracy (that is M6).
