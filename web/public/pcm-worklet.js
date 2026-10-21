// AudioWorklet processor: forwards mono Float32 chunks from the microphone to the main thread.
// Plain JavaScript on purpose: it is loaded as a same-origin module (CSP script-src 'self').
class PcmCaptureProcessor extends AudioWorkletProcessor {
  process(inputs) {
    const channel = inputs[0] && inputs[0][0];
    if (channel && channel.length > 0) {
      this.port.postMessage(channel.slice(0));
    }
    return true;
  }
}

registerProcessor("pcm-capture", PcmCaptureProcessor);
