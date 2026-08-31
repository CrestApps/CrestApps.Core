// Realtime (speech-to-speech) test harness client.
// Captures microphone audio as PCM16 @ 24 kHz, streams it to /Realtime/Stream over a WebSocket, and plays back the
// assistant's PCM16 audio. Shared by the MVC and Blazor sample hosts. Requires elements with these ids on the page:
//   rt-start, rt-stop, rt-clear, rt-status, rt-log, rt-deployment, rt-voice, rt-instructions
(function () {
    function init() {
        var startBtn = document.getElementById('rt-start');
        if (!startBtn || startBtn.dataset.rtInitialized) { return; }
        startBtn.dataset.rtInitialized = '1';

        var stopBtn = document.getElementById('rt-stop');
        var clearBtn = document.getElementById('rt-clear');
        var statusEl = document.getElementById('rt-status');
        var logEl = document.getElementById('rt-log');
        var deploymentEl = document.getElementById('rt-deployment');
        var voiceEl = document.getElementById('rt-voice');
        var instructionsEl = document.getElementById('rt-instructions');
        var profileEl = document.getElementById('rt-profile');
        var deploymentRow = document.querySelector('[data-rt-deployment-row]');
        var instructionsRow = document.querySelector('[data-rt-instructions-row]');

        function syncMode() {
            // When a profile is selected the orchestrator provides the deployment, instructions, and tools,
            // so the raw deployment/instructions inputs are not used.
            var usingProfile = profileEl && profileEl.value;
            if (deploymentRow) { deploymentRow.style.display = usingProfile ? 'none' : ''; }
            if (instructionsRow) { instructionsRow.style.display = usingProfile ? 'none' : ''; }
        }

        if (profileEl) {
            profileEl.addEventListener('change', syncMode);
            syncMode();
        }

        var SAMPLE_RATE = 24000;

        var ws = null;
        var audioContext = null;
        var micStream = null;
        var sourceNode = null;
        var processorNode = null;
        var zeroGain = null;
        var playHead = 0;
        var scheduledSources = [];
        var assistantLine = null;

        function setStatus(text, cls) {
            statusEl.textContent = text;
            statusEl.className = 'badge ' + (cls || 'bg-secondary');
        }

        function appendLine(role, text) {
            var wrapper = document.createElement('div');
            wrapper.className = 'mb-2';
            var label = document.createElement('span');
            var colors = { user: 'text-primary', assistant: 'text-success', error: 'text-danger', system: 'text-muted' };
            label.className = 'fw-bold me-1 ' + (colors[role] || 'text-muted');
            label.textContent = role === 'user' ? 'You:' : role === 'assistant' ? 'Assistant:' : role === 'error' ? 'Error:' : 'System:';
            var body = document.createElement('span');
            body.textContent = text;
            wrapper.appendChild(label);
            wrapper.appendChild(body);
            logEl.appendChild(wrapper);
            logEl.scrollTop = logEl.scrollHeight;
            return body;
        }

        function logAssistantDelta(text) {
            if (!assistantLine) { assistantLine = appendLine('assistant', ''); }
            assistantLine.textContent += text;
            logEl.scrollTop = logEl.scrollHeight;
        }

        function startMic() {
            sourceNode = audioContext.createMediaStreamSource(micStream);
            processorNode = audioContext.createScriptProcessor(4096, 1, 1);
            processorNode.onaudioprocess = function (event) {
                if (!ws || ws.readyState !== WebSocket.OPEN) { return; }
                var input = event.inputBuffer.getChannelData(0);
                var pcm = new Int16Array(input.length);
                for (var i = 0; i < input.length; i++) {
                    var s = Math.max(-1, Math.min(1, input[i]));
                    pcm[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
                }
                ws.send(pcm.buffer);
            };
            zeroGain = audioContext.createGain();
            zeroGain.gain.value = 0; // keep the processor alive without echoing the mic
            sourceNode.connect(processorNode);
            processorNode.connect(zeroGain);
            zeroGain.connect(audioContext.destination);
        }

        function playPcm(arrayBuffer) {
            if (!audioContext) { return; }
            var pcm = new Int16Array(arrayBuffer);
            if (pcm.length === 0) { return; }
            var f32 = new Float32Array(pcm.length);
            for (var i = 0; i < pcm.length; i++) { f32[i] = pcm[i] / 0x8000; }
            var buffer = audioContext.createBuffer(1, f32.length, SAMPLE_RATE);
            buffer.copyToChannel(f32, 0);
            var src = audioContext.createBufferSource();
            src.buffer = buffer;
            src.connect(audioContext.destination);
            var now = audioContext.currentTime;
            if (playHead < now) { playHead = now; }
            src.start(playHead);
            playHead += buffer.duration;
            scheduledSources.push(src);
            src.onended = function () { scheduledSources = scheduledSources.filter(function (s) { return s !== src; }); };
        }

        function flushPlayback() {
            scheduledSources.forEach(function (s) { try { s.stop(); } catch (e) { /* already stopped */ } });
            scheduledSources = [];
            if (audioContext) { playHead = audioContext.currentTime; }
        }

        function handleEvent(msg) {
            if (msg.type === 'transcript') {
                if (msg.role === 'assistant') { logAssistantDelta(msg.text); }
                else { appendLine('user', msg.text); assistantLine = null; }
            } else if (msg.type === 'error') {
                appendLine('error', msg.message);
                setStatus('Error', 'bg-danger');
            } else if (msg.type === 'event' && msg.name === 'speech_started') {
                flushPlayback();
                assistantLine = null;
            } else if (msg.type === 'ready') {
                appendLine('system', 'Connected to "' + msg.deployment + '". Start talking.');
            }
        }

        function start() {
            startBtn.disabled = true;
            setStatus('Requesting mic…', 'bg-warning');
            navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true } })
                .then(function (stream) {
                    micStream = stream;
                    var Ctx = window.AudioContext || window.webkitAudioContext;
                    audioContext = new Ctx({ sampleRate: SAMPLE_RATE });
                    playHead = audioContext.currentTime;

                    var params = new URLSearchParams({ voice: voiceEl.value });
                    if (profileEl && profileEl.value) {
                        params.set('profileId', profileEl.value);
                    } else {
                        params.set('deploymentName', deploymentEl.value);
                        params.set('instructions', instructionsEl.value);
                    }
                    var proto = location.protocol === 'https:' ? 'wss' : 'ws';
                    setStatus('Connecting…', 'bg-warning');
                    ws = new WebSocket(proto + '://' + location.host + '/Realtime/Stream?' + params.toString());
                    ws.binaryType = 'arraybuffer';

                    ws.onopen = function () {
                        setStatus('Listening', 'bg-success');
                        stopBtn.disabled = false;
                        startMic();
                    };
                    ws.onmessage = function (event) {
                        if (typeof event.data === 'string') { handleEvent(JSON.parse(event.data)); }
                        else { playPcm(event.data); }
                    };
                    ws.onerror = function () { setStatus('Connection error', 'bg-danger'); };
                    ws.onclose = function (event) {
                        if (event && event.code && event.code !== 1000 && event.code !== 1005) {
                            appendLine('error', 'Socket closed (code ' + event.code + (event.reason ? ' — ' + event.reason : '') + ').');
                        }
                        stop(true);
                    };
                })
                .catch(function () {
                    setStatus('Microphone denied', 'bg-danger');
                    startBtn.disabled = false;
                });
        }

        function stop(fromClose) {
            stopBtn.disabled = true;
            startBtn.disabled = false;
            try { if (processorNode) { processorNode.disconnect(); processorNode.onaudioprocess = null; } } catch (e) { }
            try { if (sourceNode) { sourceNode.disconnect(); } } catch (e) { }
            try { if (zeroGain) { zeroGain.disconnect(); } } catch (e) { }
            try { if (micStream) { micStream.getTracks().forEach(function (t) { t.stop(); }); } } catch (e) { }
            flushPlayback();
            try { if (audioContext) { audioContext.close(); } } catch (e) { }
            audioContext = null;
            if (!fromClose && ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
                try { ws.close(); } catch (e) { }
            }
            if (statusEl.className.indexOf('bg-danger') === -1) { setStatus('Stopped', 'bg-secondary'); }
        }

        startBtn.addEventListener('click', start);
        stopBtn.addEventListener('click', function () { stop(false); });
        clearBtn.addEventListener('click', function () { logEl.innerHTML = ''; assistantLine = null; });
    }

    // Exposed so Blazor's interactive render (which mounts the DOM after the circuit connects) can trigger init
    // from OnAfterRenderAsync. Static hosts (MVC) auto-init on DOMContentLoaded.
    window.realtimeTest = { init: init };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
