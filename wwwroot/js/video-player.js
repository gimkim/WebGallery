(() => {
  "use strict";
  const $ = id => document.getElementById(id);
  const panel = $("playerPanel");
  if (!panel) return;
  const MEDIA_TRACK_PREFERENCES_KEY = "gimGalleryMediaTrackPreferencesV1";
  const SUBTITLE_SIZE_KEY = "gimGallerySubtitleSize";
  const SUBTITLE_SIZE_MIN = 70, SUBTITLE_SIZE_MAX = 160, SUBTITLE_SIZE_STEP = 10;
  function loadMediaTrackPreferences() {
    try { const stored = JSON.parse(localStorage.getItem(MEDIA_TRACK_PREFERENCES_KEY) || "null"); if (stored && typeof stored === "object") return { files: stored.files || {}, last: stored.last || {} }; } catch {}
    return { files: {}, last: {} };
  }
  function loadSubtitleSize() { const n=Number(localStorage.getItem(SUBTITLE_SIZE_KEY)); return Number.isFinite(n) ? Math.max(SUBTITLE_SIZE_MIN,Math.min(SUBTITLE_SIZE_MAX,Math.round(n/SUBTITLE_SIZE_STEP)*SUBTITLE_SIZE_STEP)) : 100; }
  const mediaTrackPreferences = loadMediaTrackPreferences();
  const playerState = {
    item:null, metadata:null, token:0, streamToken:0, mediaSource:null, sourceBuffer:null, objectUrl:null, abortController:null,
    subtitleAbortController:null, subtitleObjectUrl:null, segmentStart:0, segmentDuration:8, bufferGoal:24, pumping:false, pumpPending:false,
    pumpTimer:null, seekTimer:null, autoplayRetryTimer:null, bufferQuotaLimited:false, bufferQuotaAhead:0, suppressSeek:false, playIntent:false,
    suppressPause:false, pauseGuard:false, pendingSeek:false, seekResumeRequested:false, seekTarget:0, seekRestarting:false,
    programmaticSeekTarget:null, suppressSeekRelease:null, allowHevcCopy:false, nativeVideoCodec:"", hevcFallbackUsed:false, statusText:"",
    streamDetails:null, segmentTimelineLead:0, compensateSegmentLead:false, continuousHevc:false, continuousTask:null, heldFramePoster:null,
    previousPoster:null, heldFrameClearTimer:null, pointerSeekFocusPending:false, pointerSeekFocusTimer:null, subtitleSize:loadSubtitleSize(),
    subtitleRenderGeneration:0, subtitleSeekPending:false
  };
  function escapeHtml(value) { return String(value ?? "").replace(/[&<>'"]/g, ch => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"}[ch])); }
  function sameMediaItem(a,b) { return !!a && !!b && a.relativePath===b.relativePath && a.scope===b.scope; }
  function syncPlayButtons() { document.querySelectorAll("[data-video-player]").forEach(button => { const active=sameMediaItem(button._mediaItem,playerState.item); button.classList.toggle("is-playing",active); button.setAttribute("aria-pressed",active ? "true":"false"); }); }
  function isVideo(item) { return !!item?.relativePath; }
  async function requestJson(url, options={}) { const response=await fetch(url,{credentials:"same-origin",...options}); const text=await response.text(); let data; try{data=text?JSON.parse(text):null;}catch{data={message:text};} if(!response.ok) throw new Error(data?.message || `HTTP ${response.status}`); return data; }
  function withParams(base, values) { const url=new URL(base,location.href); Object.entries(values).forEach(([k,v])=>{if(v!==""&&v!=null)url.searchParams.set(k,v);}); return url.href; }
  function mediaMetadataUrl(item){return item.metadataUrl;}
  function mediaStreamUrl(item,a="",s=""){return withParams(item.streamUrl,{audio:a,subtitle:s});}
  function mediaSegmentUrl(item,start,duration,a="",s=""){return withParams(item.segmentUrl,{start:Number(start).toFixed(3),duration:Number(duration).toFixed(3),hevc:playerState.allowHevcCopy?"true":"",audio:a,subtitle:s});}
  function mediaContinuousHevcUrl(item,start,a=""){return withParams(item.continuousUrl,{start:Number(start).toFixed(3),audio:a});}
  function mediaSubtitleUrl(item,s,p=""){return withParams(item.subtitleUrl,{subtitle:s,progress:p});}
  function mediaSubtitleProgressUrl(progress){return withParams(playerState.item.subtitleProgressUrl,{id:progress});}
    function formatDuration(seconds) {
      if (!Number.isFinite(Number(seconds)) || Number(seconds) <= 0) return "Unknown duration";
      const total = Math.round(Number(seconds));
      const hours = Math.floor(total / 3600);
      const minutes = Math.floor((total % 3600) / 60).toString().padStart(2, "0");
      const secs = (total % 60).toString().padStart(2, "0");
      return hours > 0 ? `${hours}:${minutes}:${secs}` : `${minutes}:${secs}`;
    }
    function formatBitRate(bitsPerSecond) {
      const value = Number(bitsPerSecond);
      if (!Number.isFinite(value) || value <= 0) return "";
      return value >= 1000000 ? `${(value / 1000000).toFixed(value >= 10000000 ? 1 : 2)} Mbps` : `${Math.round(value / 1000)} kbps`;
    }
    function streamModeLabel(details) {
      if (!details?.mode) return "";
      if (details.mode === "copy-hevc") return "Direct HEVC/hvc1 stream · no re-encode";
      if (details.mode === "copy-h264") return "Direct H.264/avc1 stream · no re-encode";
      if (details.mode === "nvenc") return "Re-encoded with NVIDIA NVENC";
      if (details.mode === "qsv-hw") return "Re-encoded with Intel Quick Sync · hardware decode/encode";
      if (details.mode === "qsv") return "Re-encoded with Intel Quick Sync · hardware encode";
      if (details.mode === "libx264") return "Re-encoded with CPU libx264 · 16 threads";
      return details.mode;
    }
    function renderPlayerStatus() {
      const parts = [];
      if (playerState.statusText) parts.push(playerState.statusText);
      const metadata = playerState.metadata, track = metadata?.video?.[0];
      if (track) {
        const codec = [String(track.codec || "").toUpperCase(), track.profile].filter(Boolean).join(" ");
        const resolution = track.width && track.height ? `${track.width}×${track.height}` : "";
        const fps = Number(track.frameRate) > 0 ? `${Number(track.frameRate).toFixed(Number(track.frameRate) >= 30 ? 1 : 2)}fps` : "";
        const sourceRate = formatBitRate(track.bitRate || metadata.sourceBitRate);
        parts.push([codec, resolution, fps, sourceRate ? `source ${sourceRate}` : ""].filter(Boolean).join(" · "));
      }
      const selectedAudioIndex = $("audioTrackSelect")?.value;
      const selectedAudio = metadata?.audio?.find(track => String(track.index) === String(selectedAudioIndex));
      if (selectedAudio) {
        const audioCodec = String(selectedAudio.codec || "unknown").toUpperCase();
        parts.push(audioCodec === "AAC" ? "AAC audio · direct stream" : `Audio ${audioCodec} → AAC`);
      }
      const details = playerState.streamDetails;
      if (details?.mode) {
        const encoding = [streamModeLabel(details), details.profile && details.level ? `${details.profile}@${details.level}` : details.profile, details.quality].filter(Boolean);
        const target = formatBitRate(Number(details.targetKbps) * 1000);
        const max = formatBitRate(Number(details.maxKbps) * 1000);
        if (target) encoding.push(`target ${target}`);
        if (max) encoding.push(`max ${max}`);
        parts.push(encoding.join(" · "));
      }
      const video = $("videoPlayer");
      if (playerState.mediaSource && Number.isFinite(video.currentTime)) {
        const end = bufferedEnd(video, video.currentTime);
        const ahead = Math.max(0, end - video.currentTime);
        const ranges = video.buffered?.length || 0;
        const quotaState = playerState.bufferQuotaLimited ? " · browser quota reached; reclaiming buffer" : "";
        parts.push(`buffer +${ahead.toFixed(1)}s / target ${playerState.bufferGoal}s${ranges ? ` · ${ranges} ranges` : ""}${quotaState}`);
      }
      $("playerMessage").textContent = parts.filter(Boolean).join("  |  ");
    }
    function setPlayerStatus(text) { playerState.statusText = text || ""; renderPlayerStatus(); }
    function renderSubtitleSize() {
      const size = playerState.subtitleSize;
      const video = $("videoPlayer");
      video.style.setProperty("--subtitle-font-size", `${size}%`);
      $("subtitleSizeValue").textContent = `${size}%`;
      $("subtitleDecreaseButton").disabled = size <= SUBTITLE_SIZE_MIN;
      $("subtitleIncreaseButton").disabled = size >= SUBTITLE_SIZE_MAX;
    }
    function changeSubtitleSize(delta) {
      const next = Math.max(SUBTITLE_SIZE_MIN, Math.min(SUBTITLE_SIZE_MAX, playerState.subtitleSize + delta));
      if (next === playerState.subtitleSize) return;
      playerState.subtitleSize = next;
      localStorage.setItem(SUBTITLE_SIZE_KEY, String(next));
      renderSubtitleSize();
    }
    function setPlayerPreparing(active, detail = "Reading video metadata. Please wait...", percent = null) {
      const overlay = $("playerPreparing"), stage = $("playerStage");
      overlay.classList.toggle("hidden", !active);
      stage.setAttribute("aria-busy", active ? "true" : "false");
      if (active) $("playerPreparingDetail").textContent = detail;
      const numericPercent = Number(percent);
      const hasPercent = active && Number.isFinite(numericPercent);
      const boundedPercent = hasPercent ? Math.max(0, Math.min(100, numericPercent)) : 0;
      $("playerPreparingProgress").classList.toggle("hidden", !hasPercent);
      $("playerPreparingProgress").setAttribute("aria-valuenow", hasPercent ? String(Math.round(boundedPercent)) : "0");
      $("playerPreparingProgressFill").style.width = `${boundedPercent}%`;
      $("playerPreparingPercent").textContent = hasPercent ? `${Math.round(boundedPercent)}%` : "";
    }
    function rememberSegmentDetails(response) {
      const mode = response.headers.get("X-Media-Mode");
      if (!mode) return;
      playerState.streamDetails = {
        mode,
        outputCodec: response.headers.get("X-Media-Output-Codec") || "",
        profile: response.headers.get("X-Media-Profile") || "",
        level: response.headers.get("X-Media-Level") || "",
        quality: response.headers.get("X-Media-Quality") || "",
        targetKbps: response.headers.get("X-Media-Target-Kbps") || "",
        maxKbps: response.headers.get("X-Media-Max-Kbps") || ""
      };
      renderPlayerStatus();
    }
    function trackLabel(track, index, kind) {
      const details = [track.title, track.language?.toUpperCase(), track.codec].filter(Boolean).join(" · ") || "Unspecified";
      const defaultText = track.isDefault ? " · default" : "";
      const unsupportedText = track.supported === false ? " · unsupported" : "";
      return `${kind} ${index + 1} · ${details}${defaultText}${unsupportedText}`;
    }
    function normalizeTrackLanguage(language) {
      const code = String(language || "").trim().toLowerCase().replace(/_/g, "-").split("-")[0];
      if (!code || code === "und" || code === "unknown") return "";
      return ({ th: "tha", tha: "tha", ja: "jpn", jp: "jpn", jpn: "jpn", en: "eng", eng: "eng" })[code] || code;
    }
    function mediaTrackPreferenceKey(item) {
      return `${String(item?.scope || "").trim().toLowerCase()}\n${String(item?.relativePath || "").replace(/\\/g, "/").toLowerCase()}`;
    }
    function findSupportedTrack(tracks, preference) {
      if (!preference) return null;
      const supported = tracks.filter(track => track.supported !== false);
      const exact = supported.find(track => String(track.index) === String(preference.index));
      if (exact) return exact;
      const language = normalizeTrackLanguage(preference.language);
      return language ? supported.find(track => normalizeTrackLanguage(track.language) === language) || null : null;
    }
    function firstSupportedTrackByLanguage(tracks, language) {
      const normalized = normalizeTrackLanguage(language);
      if (!normalized) return null;
      return tracks.find(track => track.supported !== false && normalizeTrackLanguage(track.language) === normalized) || null;
    }
    function persistMediaTrackPreferences() {
      try {
        localStorage.setItem(MEDIA_TRACK_PREFERENCES_KEY, JSON.stringify(mediaTrackPreferences));
      } catch {
        const entries = Object.entries(mediaTrackPreferences.files)
          .sort((left, right) => Number(left[1]?.updatedAt || 0) - Number(right[1]?.updatedAt || 0));
        const removeCount = Math.max(1, Math.ceil(entries.length / 4));
        entries.slice(0, removeCount).forEach(([key]) => delete mediaTrackPreferences.files[key]);
        try { localStorage.setItem(MEDIA_TRACK_PREFERENCES_KEY, JSON.stringify(mediaTrackPreferences)); } catch {}
      }
    }
    function rememberMediaTrackSelection(item, metadata, updateLastSelection) {
      if (!item || !metadata) return;
      const audioValue = $("audioTrackSelect").value;
      const subtitleValue = $("subtitleTrackSelect").value;
      const audioTrack = (metadata.audio || []).find(track => String(track.index) === String(audioValue) && track.supported !== false) || null;
      const subtitleTrack = (metadata.subtitles || []).find(track => String(track.index) === String(subtitleValue) && track.supported !== false) || null;
      mediaTrackPreferences.files[mediaTrackPreferenceKey(item)] = {
        audio: audioTrack ? { index: String(audioTrack.index), language: normalizeTrackLanguage(audioTrack.language) } : null,
        subtitle: subtitleTrack
          ? { enabled: true, index: String(subtitleTrack.index), language: normalizeTrackLanguage(subtitleTrack.language) }
          : { enabled: false },
        updatedAt: Date.now()
      };
      if (updateLastSelection) {
        mediaTrackPreferences.last.audioLanguage = audioTrack ? normalizeTrackLanguage(audioTrack.language) : "";
        mediaTrackPreferences.last.subtitleEnabled = Boolean(subtitleTrack);
        mediaTrackPreferences.last.subtitleLanguage = subtitleTrack ? normalizeTrackLanguage(subtitleTrack.language) : "";
      }
      persistMediaTrackPreferences();
    }
    function renderPlayerTracks(metadata, item) {
      const audioSelect = $("audioTrackSelect");
      const subtitleSelect = $("subtitleTrackSelect");
      const audio = metadata.audio || [];
      const subtitles = metadata.subtitles || [];
      const supportedAudio = audio.filter(track => track.supported !== false);
      const defaultAudio = audio.find(track => track.isDefault && track.supported !== false) || supportedAudio[0] || audio[0];
      const preferenceKey = mediaTrackPreferenceKey(item);
      const hasFilePreference = Object.prototype.hasOwnProperty.call(mediaTrackPreferences.files, preferenceKey);
      const filePreference = hasFilePreference ? mediaTrackPreferences.files[preferenceKey] : null;
      const selectedAudio = findSupportedTrack(audio, filePreference?.audio)
        || (!hasFilePreference ? firstSupportedTrackByLanguage(audio, mediaTrackPreferences.last.audioLanguage) : null)
        || defaultAudio;
      const selectedSubtitle = filePreference?.subtitle?.enabled
        ? findSupportedTrack(subtitles, filePreference.subtitle)
        : !hasFilePreference && mediaTrackPreferences.last.subtitleEnabled
          ? firstSupportedTrackByLanguage(subtitles, mediaTrackPreferences.last.subtitleLanguage)
          : null;
      audioSelect.innerHTML = audio.length
        ? audio.map((track, index) => `<option value="${track.index}"${String(track.index) === String(selectedAudio?.index) ? " selected" : ""}${track.supported === false ? " disabled" : ""}>${escapeHtml(trackLabel(track, index, "Audio"))}</option>`).join("")
        : `<option value="">No audio track</option>`;
      audioSelect.disabled = supportedAudio.length < 2;
      subtitleSelect.innerHTML = `<option value=""${selectedSubtitle ? "" : " selected"}>Subtitles off</option>${subtitles.map((track, index) => `<option value="${track.index}"${String(track.index) === String(selectedSubtitle?.index) ? " selected" : ""}${track.supported === false ? " disabled" : ""}>${escapeHtml(trackLabel(track, index, "Subtitles"))}</option>`).join("")}`;
      subtitleSelect.disabled = !subtitles.some(track => track.supported !== false);
      rememberMediaTrackSelection(item, metadata, false);
    }
    function waitForMediaEvent(target, eventName) {
      return new Promise((resolve, reject) => {
        const onEvent = () => { cleanup(); resolve(); };
        const onError = event => { cleanup(); reject(event?.error || new Error(`Media event failed: ${eventName}`)); };
        const cleanup = () => { target.removeEventListener(eventName, onEvent); target.removeEventListener("error", onError); };
        target.addEventListener(eventName, onEvent, { once: true });
        target.addEventListener("error", onError, { once: true });
      });
    }
    function appendBufferAsync(sourceBuffer, data) {
      return new Promise((resolve, reject) => {
        const onEnd = () => { cleanup(); resolve(); };
        const onError = event => { cleanup(); reject(event?.error || new Error("MediaSource append failed")); };
        const cleanup = () => { sourceBuffer.removeEventListener("updateend", onEnd); sourceBuffer.removeEventListener("error", onError); sourceBuffer.removeEventListener("abort", onError); };
        sourceBuffer.addEventListener("updateend", onEnd, { once: true });
        sourceBuffer.addEventListener("error", onError, { once: true });
        sourceBuffer.addEventListener("abort", onError, { once: true });
        try { sourceBuffer.appendBuffer(data); } catch (error) { onError({ error }); }
      });
    }
    function removeSourceBufferAsync(sourceBuffer, start, end) {
      if (!(end > start)) return Promise.resolve();
      return new Promise((resolve, reject) => {
        const onEnd = () => { cleanup(); resolve(); };
        const onError = event => { cleanup(); reject(event?.error || new Error("MediaSource remove failed")); };
        const cleanup = () => { sourceBuffer.removeEventListener("updateend", onEnd); sourceBuffer.removeEventListener("error", onError); sourceBuffer.removeEventListener("abort", onError); };
        sourceBuffer.addEventListener("updateend", onEnd, { once: true });
        sourceBuffer.addEventListener("error", onError, { once: true });
        sourceBuffer.addEventListener("abort", onError, { once: true });
        try { sourceBuffer.remove(start, end); } catch (error) { onError({ error }); }
      });
    }
    function waitForSourceBufferIdle(sourceBuffer, signal) {
      if (!sourceBuffer?.updating) return Promise.resolve();
      return new Promise((resolve, reject) => {
        const cleanup = () => {
          sourceBuffer.removeEventListener("updateend", check);
          sourceBuffer.removeEventListener("error", fail);
          sourceBuffer.removeEventListener("abort", fail);
          signal?.removeEventListener("abort", abort);
        };
        const check = () => {
          if (!sourceBuffer.updating) { cleanup(); resolve(); }
        };
        const fail = event => { cleanup(); reject(event?.error || new Error("MediaSource update failed")); };
        const abort = () => { cleanup(); reject(new DOMException("Obsolete media request", "AbortError")); };
        sourceBuffer.addEventListener("updateend", check);
        sourceBuffer.addEventListener("error", fail, { once: true });
        sourceBuffer.addEventListener("abort", fail, { once: true });
        signal?.addEventListener("abort", abort, { once: true });
        check();
      });
    }
    function isSourceBufferQuotaError(error) {
      return error?.name === "QuotaExceededError" || /SourceBuffer is full|cannot free space/i.test(String(error?.message || ""));
    }
    function throwIfObsoleteMediaRequest(sourceBuffer, token, signal) {
      if (signal?.aborted || token !== playerState.streamToken || sourceBuffer !== playerState.sourceBuffer) {
        throw new DOMException("Obsolete media request", "AbortError");
      }
    }
    async function trimPlayedSourceBuffer(sourceBuffer, token, force = false) {
      const signal = playerState.abortController?.signal;
      throwIfObsoleteMediaRequest(sourceBuffer, token, signal);
      const video = $("videoPlayer");
      const current = Number.isFinite(video.currentTime) ? video.currentTime : 0;
      const cutoff = Math.max(0, current - 30);
      if (cutoff <= 0 || !sourceBuffer.buffered.length) return 0;
      let bufferedStart = Number.POSITIVE_INFINITY;
      let removeEnd = 0;
      for (let index = 0; index < sourceBuffer.buffered.length; index++) {
        const start = sourceBuffer.buffered.start(index);
        const end = sourceBuffer.buffered.end(index);
        if (start >= cutoff) break;
        bufferedStart = Math.min(bufferedStart, start);
        removeEnd = Math.max(removeEnd, Math.min(end, cutoff));
      }
      const removableSeconds = removeEnd - bufferedStart;
      if (!(removeEnd > 0) || (!force && removableSeconds < 4)) return 0;
      await waitForSourceBufferIdle(sourceBuffer, signal);
      throwIfObsoleteMediaRequest(sourceBuffer, token, signal);
      await removeSourceBufferAsync(sourceBuffer, 0, removeEnd);
      return Math.max(0, removableSeconds);
    }
    function waitForBufferSpace(video, token, signal, minimumPlaybackTime) {
      return new Promise((resolve, reject) => {
        let timer = null;
        const cleanup = () => {
          video.removeEventListener("timeupdate", check);
          video.removeEventListener("seeking", check);
          signal?.removeEventListener("abort", abort);
          if (timer) clearTimeout(timer);
        };
        const finish = () => { cleanup(); resolve(); };
        const check = () => {
          if (token !== playerState.streamToken) return abort();
          if ((Number.isFinite(video.currentTime) ? video.currentTime : 0) >= minimumPlaybackTime) finish();
        };
        const abort = () => { cleanup(); reject(new DOMException("Obsolete media request", "AbortError")); };
        video.addEventListener("timeupdate", check);
        video.addEventListener("seeking", check);
        signal?.addEventListener("abort", abort, { once: true });
        timer = setTimeout(finish, 1000);
        check();
      });
    }
    async function appendBufferWithQuotaRecovery(sourceBuffer, data, token) {
      const signal = playerState.abortController?.signal;
      const video = $("videoPlayer");
      await trimPlayedSourceBuffer(sourceBuffer, token);
      while (true) {
        throwIfObsoleteMediaRequest(sourceBuffer, token, signal);
        await waitForSourceBufferIdle(sourceBuffer, signal);
        try {
          await appendBufferAsync(sourceBuffer, data);
          if (playerState.bufferQuotaLimited) {
            playerState.bufferQuotaLimited = false;
            playerState.bufferQuotaAhead = 0;
            renderPlayerStatus();
          }
          return;
        } catch (error) {
          if (!isSourceBufferQuotaError(error)) throw error;
          const blockedAt = Number.isFinite(video.currentTime) ? video.currentTime : 0;
          playerState.bufferQuotaLimited = true;
          playerState.bufferQuotaAhead = Math.max(0, bufferedEnd(video, blockedAt) - blockedAt);
          renderPlayerStatus();
          const removedSeconds = await trimPlayedSourceBuffer(sourceBuffer, token, true);
          const requiredProgress = Math.max(2, playerState.segmentDuration - removedSeconds);
          if (requiredProgress > .25) {
            const retryAt = blockedAt + requiredProgress;
            while (token === playerState.streamToken && (Number.isFinite(video.currentTime) ? video.currentTime : 0) < retryAt) {
              await waitForBufferSpace(video, token, signal, retryAt);
            }
            await trimPlayedSourceBuffer(sourceBuffer, token, true);
          }
        }
      }
    }
    function readMp4Box(bytes, offset) {
      if (offset + 8 > bytes.length) return null;
      const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
      let size = view.getUint32(offset);
      let header = 8;
      if (size === 1) { if (offset + 16 > bytes.length) return null; size = Number(view.getBigUint64(offset + 8)); header = 16; }
      else if (size === 0) size = bytes.length - offset;
      if (!Number.isSafeInteger(size) || size < header || offset + size > bytes.length) return null;
      return { start: offset, end: offset + size, type: String.fromCharCode(bytes[offset + 4], bytes[offset + 5], bytes[offset + 6], bytes[offset + 7]) };
    }
    function splitMp4Fragment(buffer) {
      const bytes = new Uint8Array(buffer); let offset = 0; let mediaStart = -1;
      while (offset < bytes.length) { const box = readMp4Box(bytes, offset); if (!box) break; if (box.type === "moof") { mediaStart = box.start; break; } offset = box.end; }
      if (mediaStart < 0) throw new Error("FFmpeg did not return an MP4 fragment");
      return { init: buffer.slice(0, mediaStart), media: buffer.slice(mediaStart) };
    }
    function findAscii(bytes, text) {
      const code = [...text].map(char => char.charCodeAt(0));
      for (let i = 0; i <= bytes.length - code.length; i++) { let match = true; for (let j = 0; j < code.length; j++) if (bytes[i + j] !== code[j]) { match = false; break; } if (match) return i; }
      return -1;
    }
    function codecMimeFromInit(init, hasAudio) {
      const bytes = new Uint8Array(init); const avcIndex = findAscii(bytes, "avcC"); const hevcIndex = findAscii(bytes, "hvcC");
      let videoCodec = "avc1.42E01E";
      if (hevcIndex >= 0) videoCodec = playerState.nativeVideoCodec || "hvc1";
      else if (avcIndex >= 0 && avcIndex + 7 < bytes.length) videoCodec = `avc1.${[bytes[avcIndex + 5], bytes[avcIndex + 6], bytes[avcIndex + 7]].map(value => value.toString(16).padStart(2, "0")).join("").toUpperCase()}`;
      const codecs = [videoCodec]; if (hasAudio) codecs.push("mp4a.40.2");
      return `video/mp4; codecs="${codecs.join(",")}"`;
    }
    function rememberPlaybackCompatibility(metadata) {
      playerState.allowHevcCopy = false;
      playerState.nativeVideoCodec = "";
      playerState.hevcFallbackUsed = false;
      playerState.statusText = "";
      playerState.streamDetails = null;
      const videoTrack = metadata?.video?.[0];
      if (!window.MediaSource || !videoTrack || !["hevc", "h265"].includes(String(videoTrack.codec || "").toLowerCase())) return;
      const main10 = /10/.test(String(videoTrack.profile || ""));
      const level = Math.max(30, Number(videoTrack.level) || 120);
      const codec = main10 ? `hvc1.2.4.L${level}.B0` : `hvc1.1.6.L${level}.B0`;
      const hasAudio = Boolean(metadata.audio?.length);
      const mime = `video/mp4; codecs="${codec}${hasAudio ? ",mp4a.40.2" : ""}"`;
      if (!MediaSource.isTypeSupported(mime)) return;
      playerState.allowHevcCopy = true;
      playerState.nativeVideoCodec = codec;
      const supportedAudio = !hasAudio || metadata.audio.some(track => String(track.codec || "").toLowerCase() === "aac");
      if (supportedAudio) {
        metadata.canStreamWithoutTranscode = true;
        metadata.streamWarning = null;
      } else {
        metadata.streamWarning = "The video streams directly; only unsupported audio is transcoded.";
      }
    }
    function disableHevcCopyForPlayback() {
      if (!playerState.allowHevcCopy || playerState.hevcFallbackUsed) return false;
      playerState.allowHevcCopy = false;
      playerState.nativeVideoCodec = "";
      playerState.hevcFallbackUsed = true;
      if (playerState.metadata) {
        playerState.metadata.canStreamWithoutTranscode = false;
        playerState.metadata.streamWarning = "HEVC MediaSource failed. Falling back to H.264...";
      }
      return true;
    }
    function concatBuffers(first, second) { const output = new Uint8Array(first.byteLength + second.byteLength); output.set(new Uint8Array(first), 0); output.set(new Uint8Array(second), first.byteLength); return output.buffer; }
    function concatByteArrays(parts) { const length = parts.reduce((sum, part) => sum + part.byteLength, 0); const output = new Uint8Array(length); let offset = 0; for (const part of parts) { output.set(part, offset); offset += part.byteLength; } return output; }
    function bufferedAt(video, time) { for (let i = 0; i < video.buffered.length; i++) if (time >= video.buffered.start(i) - .25 && time <= video.buffered.end(i) + .25) return true; return false; }
    function bufferedEnd(video, time) { for (let i = 0; i < video.buffered.length; i++) if (time >= video.buffered.start(i) - .25 && time <= video.buffered.end(i) + .25) return video.buffered.end(i); return time; }
    function alignToBufferedStart(video, target) {
      for (let i = 0; i < video.buffered.length; i++) {
        const start = video.buffered.start(i);
        if (start >= target && start - target <= .5) return start;
      }
      return target;
    }
    function clearProgrammaticSeek() {
      if (playerState.suppressSeekRelease) playerState.suppressSeekRelease();
      else {
        playerState.suppressSeek = false;
        playerState.programmaticSeekTarget = null;
      }
    }
    function setProgrammaticCurrentTime(video, time) {
      clearProgrammaticSeek();
      playerState.suppressSeek = true;
      playerState.programmaticSeekTarget = time;
      let timer = null;
      const release = () => {
        video.removeEventListener("seeked", release);
        video.removeEventListener("playing", release);
        if (timer) clearTimeout(timer);
        if (playerState.suppressSeekRelease !== release) return;
        playerState.suppressSeekRelease = null;
        playerState.suppressSeek = false;
        playerState.programmaticSeekTarget = null;
      };
      playerState.suppressSeekRelease = release;
      video.addEventListener("seeked", release);
      video.addEventListener("playing", release);
      timer = setTimeout(release, 10000);
      try { video.currentTime = time; } catch { release(); }
    }
    async function fetchMediaSegment(item, start, duration, audio, subtitle, token) {
      const response = await fetch(mediaSegmentUrl(item, start, duration, audio, subtitle), { credentials: "same-origin", cache: "no-store", signal: playerState.abortController?.signal });
      if (!response.ok) { const text = await response.text(); let data = null; try { data = text ? JSON.parse(text) : null; } catch {} const error = new Error(data?.message || `HTTP ${response.status}`); error.status = response.status; throw error; }
      if (token === playerState.streamToken) rememberSegmentDetails(response);
      const bytes = await response.arrayBuffer();
      if (token !== playerState.streamToken) throw new DOMException("Obsolete media request", "AbortError");
      const segmentMode = response.headers.get("X-Media-Mode") || "";
      const usesServerTimeline = segmentMode === "copy-hevc" || segmentMode === "copy-h264";
      const usesEncoderLead = segmentMode === "nvenc" || segmentMode === "libx264";
      const sourceStart = Number(response.headers.get("X-Media-Source-Start"));
      const presentationLead = Number(response.headers.get("X-Media-Presentation-Lead"));
      const nextStart = Number(response.headers.get("X-Media-Next-Start"));
      return {
        bytes,
        // Independent stream-copy fragments must follow the server-selected
        // keyframe boundaries. Transcoded fragments keep the requested timeline.
        sourceStart: usesServerTimeline && Number.isFinite(sourceStart) ? sourceStart : start,
        nextStart: usesServerTimeline && Number.isFinite(nextStart) && nextStart > start ? nextStart : start + duration,
        mode: segmentMode,
        presentationLead: (usesServerTimeline || usesEncoderLead) && Number.isFinite(presentationLead) ? Math.max(0, presentationLead) : 0
      };
    }
    function clearSubtitleTrack() {
      playerState.subtitleRenderGeneration++;
      playerState.subtitleSeekPending = false;
      if (playerState.subtitleAbortController) playerState.subtitleAbortController.abort();
      playerState.subtitleAbortController = null;
      const track = $("subtitleTrack");
      if (track) {
        if (track.track) track.track.mode = "disabled";
        track.remove();
      }
      if (playerState.subtitleObjectUrl) URL.revokeObjectURL(playerState.subtitleObjectUrl);
      playerState.subtitleObjectUrl = null;
    }
    function suspendSubtitleRenderingForSeek() {
      playerState.subtitleRenderGeneration++;
      playerState.subtitleSeekPending = true;
      const track = $("subtitleTrack");
      if (track?.track) track.track.mode = "disabled";
    }
    function resumeSubtitleRenderingAfterSeek() {
      if (!playerState.subtitleSeekPending) return;
      playerState.subtitleSeekPending = false;
      const track = $("subtitleTrack");
      if (track) activateSubtitleTrack(track, playerState.token, playerState.subtitleRenderGeneration);
    }
    function createProgressId() {
      if (crypto.randomUUID) return crypto.randomUUID();
      const bytes = crypto.getRandomValues(new Uint8Array(16));
      bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
      const hex = [...bytes].map(value => value.toString(16).padStart(2, "0")).join("");
      return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    }
    async function monitorSubtitleProgress(progressId, controller, token, monitor) {
      while (!monitor.done && !controller.signal.aborted && token === playerState.token) {
        try {
          const response = await fetch(mediaSubtitleProgressUrl(progressId), { credentials: "same-origin", cache: "no-store", signal: controller.signal });
          if (response.ok) {
            const progress = await response.json();
            if (monitor.done || controller.signal.aborted || token !== playerState.token) return;
            const percent = Number(progress.percent);
            const hasPercent = Number.isFinite(percent);
            const label = hasPercent ? `Preparing subtitles... about ${Math.round(percent)}%` : "Preparing subtitles...";
            setPlayerPreparing(true, label, hasPercent ? percent : null);
            setPlayerStatus(label);
            if (progress.complete || progress.failed || progress.cancelled) return;
          }
        } catch (error) {
          if (error?.name === "AbortError") return;
        }
        await new Promise(resolve => setTimeout(resolve, 300));
      }
    }
    function activateSubtitleTrack(track, token, renderGeneration = playerState.subtitleRenderGeneration) {
      const video = $("videoPlayer");
      if (!track?.track || token !== playerState.token || renderGeneration !== playerState.subtitleRenderGeneration || !track.isConnected) return;
      if (playerState.subtitleSeekPending || video.seeking) {
        playerState.subtitleSeekPending = true;
        return;
      }
      const textTrack = track.track;
      textTrack.mode = "hidden";
      requestAnimationFrame(() => {
        if (token !== playerState.token || renderGeneration !== playerState.subtitleRenderGeneration
          || playerState.subtitleSeekPending || video.seeking || !track.isConnected) return;
        textTrack.mode = "showing";
        requestAnimationFrame(() => {
          if (token !== playerState.token || renderGeneration !== playerState.subtitleRenderGeneration
            || playerState.subtitleSeekPending || video.seeking || !track.isConnected || !textTrack.cues) return;
          const currentTime = video.currentTime;
          let hasCueAtCurrentTime = false;
          for (let index = 0; index < textTrack.cues.length; index++) {
            const cue = textTrack.cues[index];
            if (currentTime >= cue.startTime && currentTime < cue.endTime) {
              hasCueAtCurrentTime = true;
              break;
            }
          }
          if (hasCueAtCurrentTime && (!textTrack.activeCues || textTrack.activeCues.length === 0)) {
            setProgrammaticCurrentTime(video, currentTime);
          }
        });
      });
    }
    async function loadSubtitleTrack(item, subtitleIndex, token) {
      clearSubtitleTrack();
      if (!item || !subtitleIndex || token !== playerState.token) return;
      const controller = new AbortController();
      const progressId = createProgressId();
      const monitor = { done: false };
      playerState.subtitleAbortController = controller;
      monitorSubtitleProgress(progressId, controller, token, monitor);
      try {
        const response = await fetch(mediaSubtitleUrl(item, subtitleIndex, progressId), { credentials: "same-origin", cache: "no-store", signal: controller.signal });
        if (!response.ok) {
          const text = await response.text(); let data = null;
          try { data = text ? JSON.parse(text) : null; } catch {}
          throw new Error(data?.message || `HTTP ${response.status}`);
        }
        const bytes = await response.arrayBuffer();
        if (controller.signal.aborted || token !== playerState.token) return;
        const objectUrl = URL.createObjectURL(new Blob([bytes], { type: "text/vtt" }));
        playerState.subtitleObjectUrl = objectUrl;
        const oldTrack = $("subtitleTrack");
        if (oldTrack) oldTrack.remove();
        const track = document.createElement("track");
        track.id = "subtitleTrack";
        track.kind = "subtitles";
        const metadata = playerState.metadata?.subtitles?.find(value => String(value.index) === String(subtitleIndex));
        track.label = metadata?.title || "Subtitle";
        track.srclang = metadata?.language || "und";
        track.src = objectUrl;
        track.default = true;
        track.addEventListener("load", () => activateSubtitleTrack(track, token), { once: true });
        $("videoPlayer").appendChild(track);
        if (track.track) track.track.mode = "hidden";
        setTimeout(() => {
          if (track.readyState === 2) activateSubtitleTrack(track, token);
        }, 0);
        setPlayerPreparing(true, "Subtitles ready. Preparing initial buffer...", 100);
      } finally {
        monitor.done = true;
        if (playerState.subtitleAbortController === controller) playerState.subtitleAbortController = null;
        if ($("videoPlayer").readyState >= 2) setPlayerPreparing(false);
      }
    }
    function holdCurrentVideoFrame(video) {
      if (playerState.heldFramePoster || !video || !video.videoWidth || !video.videoHeight) return;
      try {
        const maxWidth = 1280, maxHeight = 720;
        const scale = Math.min(1, maxWidth / video.videoWidth, maxHeight / video.videoHeight);
        const canvas = document.createElement("canvas");
        canvas.width = Math.max(1, Math.round(video.videoWidth * scale));
        canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
        const context = canvas.getContext("2d", { alpha: false });
        if (!context) return;
        context.drawImage(video, 0, 0, canvas.width, canvas.height);
        const poster = canvas.toDataURL("image/jpeg", .86);
        playerState.previousPoster = video.getAttribute("poster");
        playerState.heldFramePoster = poster;
        video.setAttribute("poster", poster);
      } catch {}
    }
    function clearHeldVideoFrame(video = $("videoPlayer")) {
      if (playerState.heldFrameClearTimer) clearTimeout(playerState.heldFrameClearTimer);
      playerState.heldFrameClearTimer = null;
      if (!playerState.heldFramePoster) return;
      if (playerState.previousPoster == null) video.removeAttribute("poster");
      else video.setAttribute("poster", playerState.previousPoster);
      playerState.heldFramePoster = null;
      playerState.previousPoster = null;
    }
    function releaseHeldVideoFrameAfterPaint(video, token) {
      if (!playerState.heldFramePoster) return;
      const poster = playerState.heldFramePoster;
      let released = false;
      const release = () => {
        if (released) return;
        released = true;
        if (token === playerState.streamToken && playerState.heldFramePoster === poster) clearHeldVideoFrame(video);
      };
      if (typeof video.requestVideoFrameCallback === "function") video.requestVideoFrameCallback(release);
      setTimeout(release, 500);
    }
    function prepareHeldVideoFrame(video) {
      if (!playerState.continuousHevc || !playerState.allowHevcCopy) return;
      holdCurrentVideoFrame(video);
      if (!playerState.heldFramePoster) return;
      if (playerState.heldFrameClearTimer) clearTimeout(playerState.heldFrameClearTimer);
      playerState.heldFrameClearTimer = setTimeout(() => {
        playerState.heldFrameClearTimer = null;
        if (!playerState.pendingSeek) clearHeldVideoFrame(video);
      }, 1500);
    }
    function armPlayerFocusAfterPointerSeek(video) {
      playerState.pointerSeekFocusPending = true;
      if (playerState.pointerSeekFocusTimer) clearTimeout(playerState.pointerSeekFocusTimer);
      playerState.pointerSeekFocusTimer = setTimeout(() => {
        playerState.pointerSeekFocusPending = false;
        playerState.pointerSeekFocusTimer = null;
      }, 1500);
      prepareHeldVideoFrame(video);
    }
    function restorePlayerFocusAfterPointerSeek(video, force = false) {
      if (!force && !playerState.pointerSeekFocusPending) return;
      playerState.pointerSeekFocusPending = false;
      if (playerState.pointerSeekFocusTimer) clearTimeout(playerState.pointerSeekFocusTimer);
      playerState.pointerSeekFocusTimer = null;
      const focusVideoHost = () => {
        try { video.blur(); } catch {}
        try { video.focus({ preventScroll: true }); } catch { video.focus(); }
      };
      queueMicrotask(() => {
        if (playerState.item && !$("playerPanel").classList.contains("hidden")) {
          focusVideoHost();
          requestAnimationFrame(() => {
            if (playerState.item && !$("playerPanel").classList.contains("hidden")) focusVideoHost();
          });
        }
      });
    }
    function handlePlayerKeyboardShortcut(event) {
      if (event.altKey || event.ctrlKey || event.metaKey) return;
      const video = $("videoPlayer");
      const panel = $("playerPanel");
      if (!playerState.item || panel.classList.contains("hidden")) return;
      const target = event.target;
      if (target instanceof HTMLElement && target !== video
        && (target.isContentEditable || target.matches("input, textarea, select, button, a"))) return;
      const path = typeof event.composedPath === "function" ? event.composedPath() : [];
      if (target !== video && document.activeElement !== video && !path.includes(video)) return;
      if (event.code === "Space" || event.key === " ") {
        event.preventDefault();
        event.stopPropagation();
        if (video.paused || video.ended) {
          playerState.playIntent = true;
          video.play().catch(() => setPlayerStatus("Press Play to start the video."));
        } else {
          video.pause();
        }
        return;
      }
      if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
      event.preventDefault();
      event.stopPropagation();
      const current = Number.isFinite(video.currentTime) ? video.currentTime : 0;
      const duration = Number(playerState.metadata?.duration);
      const delta = event.key === "ArrowLeft" ? -10 : 10;
      const targetTime = Number.isFinite(duration) && duration > 0
        ? Math.min(Math.max(0, current + delta), duration)
        : Math.max(0, current + delta);
      if (Math.abs(targetTime - current) > 0.001) {
        if (!bufferedAt(video, targetTime)) prepareHeldVideoFrame(video);
        video.currentTime = targetTime;
      }
    }
    function cancelMediaWork() {
      clearProgrammaticSeek();
      if (playerState.abortController) playerState.abortController.abort();
      if (playerState.seekTimer) clearTimeout(playerState.seekTimer);
      if (playerState.pumpTimer) clearInterval(playerState.pumpTimer);
      if (playerState.pointerSeekFocusTimer) clearTimeout(playerState.pointerSeekFocusTimer);
      playerState.seekTimer = null;
      playerState.pumpTimer = null;
      playerState.pointerSeekFocusPending = false;
      playerState.pointerSeekFocusTimer = null;
      playerState.abortController = null; playerState.streamToken++; playerState.pumping = false; playerState.pumpPending = false;
      playerState.pendingSeek = false;
      playerState.seekResumeRequested = false;
      playerState.seekRestarting = false;
      playerState.seekTarget = 0;
      playerState.bufferQuotaLimited = false;
      playerState.bufferQuotaAhead = 0;
      playerState.continuousHevc = false;
      playerState.continuousTask = null;
      return playerState.streamToken;
    }
    function disposeMediaSource(cancelWork = true) {
      if (cancelWork) cancelMediaWork();
      if (playerState.autoplayRetryTimer) clearTimeout(playerState.autoplayRetryTimer);
      playerState.autoplayRetryTimer = null;
      if (playerState.mediaSource && playerState.mediaSource.readyState === "open") { try { playerState.mediaSource.endOfStream(); } catch {} }
      const oldUrl = playerState.objectUrl; playerState.mediaSource = null; playerState.sourceBuffer = null; playerState.objectUrl = null;
      const video = $("videoPlayer"); playerState.suppressPause = true; video.pause(); video.removeAttribute("src"); video.load(); playerState.suppressPause = false; if (oldUrl) URL.revokeObjectURL(oldUrl);
    }
    async function appendMediaSegment(fragment, start, token, resetTimeline, segmentMode, presentationLead) {
      if (token !== playerState.streamToken || !playerState.sourceBuffer) throw new DOMException("Obsolete media request", "AbortError");
      const sourceBuffer = playerState.sourceBuffer;
      await waitForSourceBufferIdle(sourceBuffer, playerState.abortController?.signal);
      if (token !== playerState.streamToken || !playerState.sourceBuffer) throw new DOMException("Obsolete media request", "AbortError");
      const video = $("videoPlayer");
      const copyMode = segmentMode === "copy-h264" || segmentMode === "copy-hevc";
      // Keep the first H.264 fragment's small decoder-reorder lead intact so its
      // timestamps never move before zero. At later joins, remove that lead from
      // timestampOffset so it does not become a new gap on every independent
      // fragment and progressively move video behind the source subtitle clock.
      const normalizedLead = Number.isFinite(presentationLead) ? Math.max(0, presentationLead) : 0;
      const copyResetLead = segmentMode === "copy-hevc" ? normalizedLead : 0;
      const copyJoinLead = copyMode ? normalizedLead : 0;
      const encoderLead = !copyMode && Number.isFinite(presentationLead) ? Math.max(0, presentationLead) : 0;
      const resetTimelineLead = copyResetLead || encoderLead;
      const joinTimelineLead = copyJoinLead || encoderLead;
      if (resetTimeline) {
        sourceBuffer.timestampOffset = start - resetTimelineLead;
        playerState.segmentTimelineLead = joinTimelineLead;
        // Compatibility fallback for an older API that did not report the
        // encoder's B-frame presentation lead.
        playerState.compensateSegmentLead = !copyMode && resetTimelineLead <= 0;
      } else {
        // Each response is a new zero-based MP4 presentation. Place it exactly at
        // the end of the buffered range containing the playhead so rounding between
        // audio/video tracks cannot leave a tiny gap that stalls playback.
        const rangeEnd = bufferedEnd(video, video.currentTime);
        const joinAt = rangeEnd > video.currentTime + .001 ? rangeEnd : start;
        sourceBuffer.timestampOffset = joinAt - (joinTimelineLead || (playerState.compensateSegmentLead ? playerState.segmentTimelineLead : 0));
      }
      // Every API response is a standalone fragmented MP4 produced by its own FFmpeg
      // process. Keep its initialization boxes with its media boxes; reusing only the
      // first response's init can leave MSE with stale track/decode state.
      await appendBufferWithQuotaRecovery(sourceBuffer, concatBuffers(fragment.init, fragment.media), token);
      if (resetTimeline && playerState.compensateSegmentLead && video.buffered.length) {
        const actualStart = alignToBufferedStart(video, start);
        playerState.segmentTimelineLead = Math.max(-.5, Math.min(.5, actualStart - start));
      }
    }
    function scheduleAutoplayRetry(video, token) {
      if (playerState.autoplayRetryTimer) clearTimeout(playerState.autoplayRetryTimer);
      playerState.autoplayRetryTimer = setTimeout(async () => {
        playerState.autoplayRetryTimer = null;
        if (token !== playerState.streamToken || !playerState.playIntent || !video.paused) return;
        try {
          await video.play();
          if (token === playerState.streamToken) { playerState.pauseGuard = false; syncPlayButtons(); }
        } catch {
          if (token === playerState.streamToken) {
            playerState.pauseGuard = false;
            setPlayerStatus("Press Play to start the video.");
            syncPlayButtons();
          }
        }
      }, 120);
    }
    async function resumeVideo(video, shouldResume, token) {
      if (!shouldResume || token !== playerState.streamToken) {
        if (token === playerState.streamToken) playerState.pauseGuard = false;
        return;
      }
      video.autoplay = true;
      try {
        await video.play();
        if (token === playerState.streamToken) {
          playerState.playIntent = true;
          playerState.pauseGuard = false;
          syncPlayButtons();
        }
      } catch {
        if (token === playerState.streamToken) {
          playerState.playIntent = true;
          playerState.pauseGuard = false;
          syncPlayButtons();
          scheduleAutoplayRetry(video, token);
        }
      }
    }
    async function pumpMediaSegments(token) {
      if (playerState.continuousHevc) return;
      if (token !== playerState.streamToken || !playerState.item || !playerState.sourceBuffer) return;
      if (playerState.pumping) { playerState.pumpPending = true; return; }
      playerState.pumping = true;
      playerState.pumpPending = false;
      try {
        const item = playerState.item, metadata = playerState.metadata, video = $("videoPlayer");
        const audio = $("audioTrackSelect").value;
        const total = Number(metadata.duration);
        let loadedSegments = 0;
        while (token === playerState.streamToken && playerState.sourceBuffer && loadedSegments < 6) {
          const target = Number.isFinite(video.currentTime) ? video.currentTime : playerState.segmentStart;
          const end = bufferedEnd(video, target);
          const goal = Number.isFinite(total) && total > 0 ? Math.min(total, target + playerState.bufferGoal) : target + playerState.bufferGoal;
          // The request cursor can run ahead of the actual MediaSource range when a
          // stream-copy fragment contains slightly less media than its nominal duration.
          // Only the browser's real buffered end can tell us that the refill goal is met.
          if (end >= goal - .2) break;
          const segmentStart = playerState.segmentStart;
          const segmentDuration = Number.isFinite(total) && total > segmentStart ? Math.min(playerState.segmentDuration, total - segmentStart) : playerState.segmentDuration;
          if (segmentDuration <= .05) break;
          const segment = await fetchMediaSegment(item, segmentStart, segmentDuration, audio, "", token);
          const fragment = splitMp4Fragment(segment.bytes); await appendMediaSegment(fragment, segment.sourceStart, token, false, segment.mode, segment.presentationLead); playerState.segmentStart = segment.nextStart;
          loadedSegments++;
        }
        if (Number.isFinite(total) && playerState.segmentStart >= total - .2 && playerState.mediaSource?.readyState === "open") { try { playerState.mediaSource.endOfStream(); } catch {} }
      } catch (error) {
        if (error?.name !== "AbortError" && token === playerState.streamToken) setPlayerStatus(error.message || "Could not load the video segment.");
      } finally {
        if (token === playerState.streamToken) {
          playerState.pumping = false;
          const rerun = playerState.pumpPending;
          playerState.pumpPending = false;
          if (rerun) queueMicrotask(() => pumpMediaSegments(token));
        }
      }
    }
    function startMediaPumpWatchdog(token) {
      if (playerState.pumpTimer) clearInterval(playerState.pumpTimer);
      playerState.pumpTimer = setInterval(() => {
        if (token !== playerState.streamToken || !playerState.sourceBuffer) {
          clearInterval(playerState.pumpTimer);
          playerState.pumpTimer = null;
          return;
        }
        const video = $("videoPlayer");
        if (playerState.playIntent || !video.paused) pumpMediaSegments(token);
      }, 1000);
    }
    async function startContinuousHevcStream(start, autoplay) {
      const item = playerState.item, metadata = playerState.metadata;
      if (!item || !metadata || !window.MediaSource) throw new Error("This browser does not support HEVC MediaSource.");
      const video = $("videoPlayer"), audio = $("audioTrackSelect").value;
      const total = Number(metadata.duration);

      if (playerState.mediaSource && playerState.sourceBuffer) holdCurrentVideoFrame(video);
      disposeMediaSource();
      const token = playerState.streamToken;
      const controller = new AbortController();
      playerState.abortController = controller;
      playerState.continuousHevc = true;
      playerState.playIntent = Boolean(autoplay);
      playerState.pauseGuard = Boolean(autoplay);
      playerState.pendingSeek = true;
      playerState.seekRestarting = true;
      playerState.seekResumeRequested = Boolean(autoplay);
      playerState.seekTarget = start;
      setPlayerStatus("Opening continuous HEVC stream...");

      const responseTask = fetch(mediaContinuousHevcUrl(item, start, audio), {
        credentials: "same-origin", cache: "no-store", signal: controller.signal
      });

      const mediaSource = new MediaSource();
      playerState.mediaSource = mediaSource;
      playerState.objectUrl = URL.createObjectURL(mediaSource);
      video.src = playerState.objectUrl;
      video.load();
      await waitForMediaEvent(mediaSource, "sourceopen");
      if (token !== playerState.streamToken) throw new DOMException("Obsolete media request", "AbortError");
      if (Number.isFinite(total) && total > 0 && mediaSource.readyState === "open") {
        try { mediaSource.duration = total; } catch {}
      }
      setProgrammaticCurrentTime(video, start);

      const response = await responseTask;
      if (!response.ok || !response.body) {
        const text = await response.text(); let data = null;
        try { data = text ? JSON.parse(text) : null; } catch {}
        throw new Error(data?.message || `HTTP ${response.status}`);
      }
      if (token !== playerState.streamToken) throw new DOMException("Obsolete media request", "AbortError");
      rememberSegmentDetails(response);
      const sourceStart = Number(response.headers.get("X-Media-Source-Start"));
      const presentationLead = Number(response.headers.get("X-Media-Presentation-Lead"));
      const timelineStart = Number.isFinite(sourceStart) ? sourceStart : start;
      const lead = Number.isFinite(presentationLead) ? Math.max(0, presentationLead) : 0;

      let readyResolve, readyReject, readySettled = false;
      const ready = new Promise((resolve, reject) => { readyResolve = resolve; readyReject = reject; });
      const consume = async () => {
        const reader = response.body.getReader();
        let pending = new Uint8Array(0), initParts = [], mediaParts = [], initialized = false;
        try {
          while (token === playerState.streamToken) {
            const { value, done } = await reader.read();
            if (done) break;
            pending = concatByteArrays([pending, value]);
            let offset = 0;
            while (offset < pending.byteLength) {
              const box = readMp4Box(pending, offset);
              if (!box) break;
              const bytes = pending.slice(box.start, box.end);
              offset = box.end;
              if (!initialized && box.type !== "moof") {
                initParts.push(bytes);
                continue;
              }
              if (!initialized) {
                const init = concatByteArrays(initParts);
                const hasAudio = Boolean(audio);
                const mime = codecMimeFromInit(init.buffer, hasAudio);
                if (!MediaSource.isTypeSupported(mime)) throw new Error("This browser does not support this HEVC stream.");
                const sourceBuffer = mediaSource.addSourceBuffer(mime);
                sourceBuffer.mode = "segments";
                sourceBuffer.timestampOffset = timelineStart - lead;
                playerState.sourceBuffer = sourceBuffer;
                await appendBufferAsync(sourceBuffer, init.buffer);
                initialized = true;
              }
              mediaParts.push(bytes);
              if (box.type !== "mdat") continue;
              const sourceBuffer = playerState.sourceBuffer;
              if (!sourceBuffer || token !== playerState.streamToken) throw new DOMException("Obsolete media request", "AbortError");
              await appendBufferWithQuotaRecovery(sourceBuffer, concatByteArrays(mediaParts).buffer, token);
              mediaParts = [];
              if (!readySettled && video.buffered.length) {
                readySettled = true;
                if (Number.isFinite(total) && total > 0 && mediaSource.readyState === "open") mediaSource.duration = total;
                setProgrammaticCurrentTime(video, start);
                readyResolve();
              }
              while (token === playerState.streamToken && bufferedEnd(video, video.currentTime) > video.currentTime + playerState.bufferGoal + 8) {
                await new Promise((resolve, reject) => {
                  const timer = setTimeout(resolve, 200);
                  controller.signal.addEventListener("abort", () => { clearTimeout(timer); reject(new DOMException("Aborted", "AbortError")); }, { once: true });
                });
              }
            }
            if (offset) pending = pending.slice(offset);
          }
          if (!readySettled) throw new Error("The HEVC stream returned no media fragment.");
          if (token === playerState.streamToken && mediaSource.readyState === "open") { try { mediaSource.endOfStream(); } catch {} }
        } catch (error) {
          if (!readySettled) { readySettled = true; readyReject(error); }
          else if (error?.name !== "AbortError" && token === playerState.streamToken) {
            playerState.continuousHevc = false;
            const target = Number.isFinite(video.currentTime) ? video.currentTime : start;
            const shouldResume = playerState.playIntent || !video.paused;
            if (disableHevcCopyForPlayback()) startMseStreamWithCodecFallback(target, shouldResume, true).catch(fallback => { if (fallback?.name !== "AbortError") setPlayerStatus(fallback.message || "HEVC streaming failed."); });
          }
        } finally {
          try { reader.releaseLock(); } catch {}
        }
      };
      playerState.continuousTask = consume();
      await ready;
      if (token !== playerState.streamToken) throw new DOMException("Obsolete media request", "AbortError");
      playerState.pendingSeek = false;
      playerState.seekRestarting = false;
      playerState.seekResumeRequested = false;
      resumeSubtitleRenderingAfterSeek();
      setPlayerStatus("Playing · continuous HEVC stream");
      await resumeVideo(video, autoplay, token);
    }
    async function seekExistingMediaSource(startTime, autoplay) {
      const item = playerState.item, metadata = playerState.metadata;
      const mediaSource = playerState.mediaSource, sourceBuffer = playerState.sourceBuffer, video = $("videoPlayer");
      if (!item || !metadata || !mediaSource || !sourceBuffer) return startMseStreamWithCodecFallback(startTime, autoplay);
      const total = Number(metadata.duration);
      const start = Number.isFinite(total) && total > 0
        ? Math.min(Math.max(0, Number(startTime) || 0), Math.max(0, total - .05))
        : Math.max(0, Number(startTime) || 0);
      const token = cancelMediaWork();
      if (playerState.mediaSource !== mediaSource || playerState.sourceBuffer !== sourceBuffer) return;
      const controller = new AbortController();
      playerState.abortController = controller;
      playerState.pendingSeek = true;
      playerState.pauseGuard = Boolean(autoplay);
      playerState.segmentStart = start;
      setPlayerStatus("Loading buffer at the seek position...");
      playerState.suppressSeek = true;
      try { video.currentTime = start; } catch {}
      playerState.suppressSeek = false;
      try {
        const audio = $("audioTrackSelect").value;
        const duration = Number.isFinite(total) && total > start ? Math.min(playerState.segmentDuration, total - start) : playerState.segmentDuration;
        const segment = await fetchMediaSegment(item, start, duration, audio, "", token);
        const fragment = splitMp4Fragment(segment.bytes);
        if (playerState.mediaSource !== mediaSource || playerState.sourceBuffer !== sourceBuffer) throw new DOMException("Obsolete media request", "AbortError");
        await appendMediaSegment(fragment, segment.sourceStart, token, true, segment.mode, segment.presentationLead);
        if (Number.isFinite(total) && total > 0 && mediaSource.readyState !== "closed") mediaSource.duration = total;
        playerState.segmentStart = segment.nextStart;
        playerState.suppressSeek = true;
        try { video.currentTime = alignToBufferedStart(video, start); } catch {}
        playerState.suppressSeek = false;
        playerState.pendingSeek = false;
        resumeSubtitleRenderingAfterSeek();
        setPlayerStatus("Ready to play");
        startMediaPumpWatchdog(token);
        pumpMediaSegments(token);
        await resumeVideo(video, autoplay, token);
      } catch (error) {
        playerState.suppressSeek = false;
        playerState.pendingSeek = false;
        if (error?.name !== "AbortError" && token === playerState.streamToken) {
          playerState.pauseGuard = false;
          if (mediaSource.readyState === "ended" || error?.name === "InvalidStateError") {
            startMseStreamWithCodecFallback(start, autoplay).catch(fallbackError => { if (fallbackError?.name !== "AbortError") setPlayerStatus(fallbackError.message || "Could not seek the video."); });
          } else {
            setPlayerStatus(error.message || "Could not seek the video.");
          }
        }
      } finally {
        if (playerState.abortController === controller) playerState.abortController = null;
        if (token === playerState.streamToken) {
          playerState.pendingSeek = false;
          resumeSubtitleRenderingAfterSeek();
        }
      }
    }
    async function startMseStream(startTime, autoplay, preserveTimeline = false) {
      const item = playerState.item, metadata = playerState.metadata; if (!item || !metadata) return;
      const video = $("videoPlayer"), audio = $("audioTrackSelect").value;
      const total = Number(metadata.duration);
      const start = Number.isFinite(total) && total > 0 ? Math.min(Math.max(0, Number(startTime) || 0), Math.max(0, total - .05)) : Math.max(0, Number(startTime) || 0);
      const firstDuration = Math.min(playerState.segmentDuration, Number.isFinite(total) ? Math.max(.05, total - start) : playerState.segmentDuration);
      let token;
      let firstSegment = null;

      playerState.playIntent = Boolean(autoplay);
      playerState.pauseGuard = Boolean(autoplay);
      playerState.seekResumeRequested = Boolean(autoplay);
      playerState.seekTarget = start;

      if (playerState.allowHevcCopy) return startContinuousHevcStream(start, autoplay);

      if (preserveTimeline && window.MediaSource) {
        token = cancelMediaWork();
        playerState.pendingSeek = true;
        playerState.seekRestarting = true;
        playerState.pauseGuard = Boolean(autoplay);
        playerState.seekResumeRequested = Boolean(autoplay);
        playerState.seekTarget = start;
        playerState.abortController = new AbortController();
        playerState.segmentStart = start;
        setPlayerStatus(metadata.canStreamWithoutTranscode === false ? (metadata.streamWarning || "Transcoding only this buffered segment...") : "Loading buffer at the seek position...");
        firstSegment = await fetchMediaSegment(item, start, firstDuration, audio, "", token);
        if (token !== playerState.streamToken) return;
        disposeMediaSource(false);
      } else {
        disposeMediaSource();
        token = playerState.streamToken;
        playerState.pendingSeek = false;
        playerState.seekRestarting = false;
        playerState.pauseGuard = Boolean(autoplay);
        playerState.abortController = new AbortController();
        playerState.segmentStart = start;
        setPlayerStatus(metadata.canStreamWithoutTranscode === false ? (metadata.streamWarning || "Transcoding only this buffered segment...") : "Loading buffer at the playback position...");
      }

      if (!window.MediaSource) {
        video.src = mediaStreamUrl(item, audio, "");
        video.load();
        setPlayerStatus("This browser does not support the on-demand player.");
        await resumeVideo(video, autoplay, token);
        return;
      }

      const mediaSource = new MediaSource();
      playerState.mediaSource = mediaSource;
      playerState.objectUrl = URL.createObjectURL(mediaSource);
      video.src = playerState.objectUrl;
      video.load();
      await waitForMediaEvent(mediaSource, "sourceopen");
      if (token !== playerState.streamToken) return;

      if (Number.isFinite(total) && total > 0) { try { mediaSource.duration = total; } catch {} }
      if (!firstSegment) firstSegment = await fetchMediaSegment(item, start, firstDuration, audio, "", token);
      const first = splitMp4Fragment(firstSegment.bytes);
      const hasAudio = Boolean(audio); const mime = codecMimeFromInit(first.init, hasAudio); const supportedMime = MediaSource.isTypeSupported(mime) ? mime : "video/mp4";
      if (!MediaSource.isTypeSupported(supportedMime)) throw new Error("This browser does not support H.264/AAC MP4.");
      playerState.sourceBuffer = mediaSource.addSourceBuffer(supportedMime);
      // API responses are independent MP4 presentations with their own init boxes.
      // Segments mode plus an explicit timestampOffset gives deterministic joins;
      // sequence mode can leave sub-frame gaps or accumulate an internal offset.
      playerState.sourceBuffer.mode = "segments";
      await appendMediaSegment(first, firstSegment.sourceStart, token, true, firstSegment.mode, firstSegment.presentationLead);
      playerState.segmentStart = firstSegment.nextStart;
      if (Number.isFinite(total) && total > 0 && mediaSource.readyState !== "closed") mediaSource.duration = total;
      if (start > .05) setProgrammaticCurrentTime(video, alignToBufferedStart(video, start));
      setPlayerStatus("Ready to play");
      startMediaPumpWatchdog(token);
      pumpMediaSegments(token);
      await resumeVideo(video, autoplay, token);
      if (token === playerState.streamToken) {
        playerState.pendingSeek = false;
        playerState.seekResumeRequested = false;
        playerState.seekRestarting = false;
        resumeSubtitleRenderingAfterSeek();
      }
    }
    async function startMseStreamWithCodecFallback(startTime, autoplay, preserveTimeline = false) {
      try {
        return await startMseStream(startTime, autoplay, preserveTimeline);
      } catch (error) {
        if (!disableHevcCopyForPlayback()) throw error;
        setPlayerStatus(playerState.metadata?.streamWarning || "Falling back to H.264...");
        return startMseStream(startTime, autoplay, preserveTimeline);
      }
    }
    function loadPlayerStream(preservePosition, forceAutoplay = false) {
      const item = playerState.item, metadata = playerState.metadata; if (!item || !metadata) return;
      const video = $("videoPlayer"); const previousTime = preservePosition && Number.isFinite(video.currentTime) ? video.currentTime : 0; const wasPlaying = forceAutoplay || (preservePosition && (playerState.playIntent || !video.paused));
      if (wasPlaying) playerState.playIntent = true;
      startMseStreamWithCodecFallback(previousTime, wasPlaying).catch(error => {
        playerState.pauseGuard = false;
        playerState.playIntent = false;
        syncPlayButtons();
        if (error?.name !== "AbortError") {
          setPlayerPreparing(false);
          setPlayerStatus(error.message || "Could not open the video.");
        }
      });
    }
    async function openPlayer(item) {
      if (!item || !isVideo(item)) return;
      const token = ++playerState.token;
      playerState.item = item;
      playerState.metadata = null;
      playerState.playIntent = false;
      playerState.pauseGuard = false;
      $("playerPanel").classList.remove("hidden");
      setPlayerPreparing(true);
      $("playerTitle").textContent = item.name;
      $("playerDownload").href = item.downloadUrl;
      $("playerDownload").hidden = false;
      $("playerMeta").textContent = item.relativePath;
      playerState.statusText = "";
      playerState.streamDetails = null;
      setPlayerStatus("Reading media tracks...");
      clearSubtitleTrack();
      clearHeldVideoFrame();
      disposeMediaSource();
      try {
        const metadata = await requestJson(mediaMetadataUrl(item));
        if (token !== playerState.token) return;
        rememberPlaybackCompatibility(metadata);
        playerState.metadata = metadata;
        renderPlayerTracks(metadata, item);
        $("playerMeta").textContent = `${item.relativePath} · ${formatDuration(metadata.duration)}`;
        setPlayerStatus(metadata.streamWarning || "Preparing stream...");
        setPlayerPreparing(true, $("subtitleTrackSelect").value ? "Preparing subtitles and initial buffer..." : "Preparing initial buffer...");
        await loadSubtitleTrack(item, $("subtitleTrackSelect").value, token);
        playerState.playIntent = true;
        $("videoPlayer").autoplay = true;
        loadPlayerStream(false, true);
      } catch (error) {
        if (token !== playerState.token) return;
        playerState.playIntent = false;
        playerState.pauseGuard = false;
        syncPlayButtons();
        setPlayerPreparing(false);
        setPlayerStatus(error.message);
        $("audioTrackSelect").disabled = true;
        $("subtitleTrackSelect").disabled = true;
      }

    }
    function closePlayer() {
      setFullWindow(false);
      playerState.token++;
      playerState.playIntent = false;
      playerState.pauseGuard = false;
      $("videoPlayer").autoplay = false;
      playerState.item = null;
      playerState.metadata = null;
      playerState.allowHevcCopy = false;
      playerState.nativeVideoCodec = "";
      playerState.hevcFallbackUsed = false;
      clearSubtitleTrack();
      disposeMediaSource();
      clearHeldVideoFrame();
      setPlayerPreparing(false);
      $("playerPanel").classList.add("hidden");
      if ($("playerPanel").open) $("playerPanel").close();
      syncPlayButtons();
    }
    function setFullWindow(enabled) {
      const panel = $("playerPanel");
      const button = $("fullWindowButton");
      panel.classList.toggle("full-window", enabled);
      document.body.classList.toggle("player-window-open", enabled);
      button.textContent = enabled ? "Exit full window" : "Full window";
      button.setAttribute("aria-pressed", enabled ? "true" : "false");
    }
    function toggleFullWindow() {
      setFullWindow(!$("playerPanel").classList.contains("full-window"));
    }
    async function toggleFullscreen() {
      try {
        if (document.fullscreenElement) await document.exitFullscreen();
        else await $("playerPanel").requestFullscreen();
      } catch (error) {
        setPlayerStatus("The browser did not allow fullscreen.");
      }
    }
  renderSubtitleSize();
  $("subtitleDecreaseButton").addEventListener("click",()=>changeSubtitleSize(-SUBTITLE_SIZE_STEP));
  $("subtitleIncreaseButton").addEventListener("click",()=>changeSubtitleSize(SUBTITLE_SIZE_STEP));
  $("closePlayerButton").addEventListener("click",closePlayer);
  $("fullWindowButton").addEventListener("click",toggleFullWindow);
  document.querySelectorAll("[data-video-player]").forEach(button=>{
    const item={name:button.dataset.videoName,relativePath:button.dataset.videoPath,scope:button.dataset.videoScope,
      metadataUrl:button.dataset.mediaMetadata,streamUrl:button.dataset.mediaStream,segmentUrl:button.dataset.mediaSegment,downloadUrl:button.dataset.mediaDownload,
      continuousUrl:button.dataset.mediaContinuous,subtitleUrl:button.dataset.mediaSubtitle,subtitleProgressUrl:button.dataset.mediaSubtitleProgress};
    button._mediaItem=item;
    button.addEventListener("click",event=>{event.preventDefault();event.stopPropagation(); if(sameMediaItem(item,playerState.item)&&!$("videoPlayer").paused)$("videoPlayer").pause(); else {panel.showModal?.();openPlayer(item);} });
    if(button.dataset.videoAutoOpen==="true")queueMicrotask(()=>{panel.showModal?.();openPlayer(item);});
  });
  $("audioTrackSelect").addEventListener("change",()=>{rememberMediaTrackSelection(playerState.item,playerState.metadata,true);loadPlayerStream(true);});
  $("subtitleTrackSelect").addEventListener("change",()=>{const item=playerState.item,token=playerState.token,subtitle=$("subtitleTrackSelect").value;rememberMediaTrackSelection(item,playerState.metadata,true);loadSubtitleTrack(item,subtitle,token).catch(error=>{if(error?.name!=="AbortError"&&token===playerState.token){setPlayerPreparing(false);setPlayerStatus(error.message||"Could not load subtitles.");}});});
  const video=$("videoPlayer");
  video.addEventListener("play",()=>{playerState.playIntent=true;setPlayerStatus("Playing");syncPlayButtons();pumpMediaSegments(playerState.streamToken);});
  video.addEventListener("pause",()=>{if(!playerState.suppressPause&&!playerState.pauseGuard&&!playerState.pendingSeek&&!video.seeking&&playerState.mediaSource){playerState.playIntent=false;video.autoplay=false;}if(!playerState.pendingSeek&&!playerState.seekRestarting)setPlayerStatus("Paused");syncPlayButtons();});
  video.addEventListener("ended",()=>{playerState.playIntent=false;video.autoplay=false;setPlayerStatus("Playback ended");syncPlayButtons();});
  video.addEventListener("error",()=>{if(!playerState.item||!playerState.metadata)return;if(!disableHevcCopyForPlayback()){setPlayerPreparing(false);setPlayerStatus("The browser could not play this video or the server could not deliver a segment.");return;}const target=Number.isFinite(video.currentTime)?video.currentTime:playerState.seekTarget;const resume=playerState.playIntent||playerState.pauseGuard||!video.paused;setPlayerPreparing(true,"Switching to browser-compatible H.264...");startMseStreamWithCodecFallback(target,resume,true).catch(error=>{playerState.pauseGuard=false;if(error?.name!=="AbortError"){setPlayerPreparing(false);setPlayerStatus(error.message||"Could not open the video.");}});});
  ["loadeddata","canplay"].forEach(name=>video.addEventListener(name,()=>setPlayerPreparing(false)));
  video.addEventListener("playing",()=>{setPlayerPreparing(false);setPlayerStatus("Playing");resumeSubtitleRenderingAfterSeek();releaseHeldVideoFrameAfterPaint(video,playerState.streamToken);});
  video.addEventListener("waiting",()=>{setPlayerStatus("Waiting for buffer...");pumpMediaSegments(playerState.streamToken);});
  video.addEventListener("stalled",()=>{setPlayerStatus("Stream stalled; refilling buffer...");pumpMediaSegments(playerState.streamToken);});
  ["progress","timeupdate"].forEach(name=>video.addEventListener(name,()=>{renderPlayerStatus();pumpMediaSegments(playerState.streamToken);}));
  video.addEventListener("pointerdown",()=>armPlayerFocusAfterPointerSeek(video));
  video.addEventListener("seeking",()=>{restorePlayerFocusAfterPointerSeek(video,!playerState.suppressSeek);if(playerState.suppressSeek){const p=Number(playerState.programmaticSeekTarget);if(Number.isFinite(p)&&Math.abs(video.currentTime-p)<.35)return;clearProgrammaticSeek();}suspendSubtitleRenderingForSeek();if(!playerState.item||!playerState.metadata||!playerState.mediaSource||!playerState.sourceBuffer)return;if(playerState.seekTimer)clearTimeout(playerState.seekTimer);const was=playerState.pendingSeek,resume=was?playerState.seekResumeRequested:(playerState.playIntent||playerState.pauseGuard||!video.paused),target=video.currentTime;setPlayerStatus("Seeking...");playerState.pendingSeek=true;playerState.seekResumeRequested=resume;playerState.seekTarget=target;if(resume){playerState.playIntent=true;playerState.pauseGuard=true;}if(bufferedAt(video,target)){pumpMediaSegments(playerState.streamToken);if(resume&&video.paused)resumeVideo(video,true,playerState.streamToken);return;}prepareHeldVideoFrame(video);playerState.seekTimer=setTimeout(()=>startMseStreamWithCodecFallback(target,resume,true).catch(error=>{playerState.pauseGuard=false;playerState.playIntent=false;playerState.pendingSeek=false;playerState.seekResumeRequested=false;playerState.seekRestarting=false;syncPlayButtons();if(error?.name!=="AbortError")setPlayerStatus(error.message||"Could not seek.");}),120);});
  video.addEventListener("seeked",()=>{if(!playerState.pendingSeek||playerState.seekRestarting){if(!playerState.pendingSeek&&!playerState.seekRestarting)resumeSubtitleRenderingAfterSeek();return;}const resume=playerState.seekResumeRequested;playerState.pendingSeek=false;playerState.seekResumeRequested=false;resumeSubtitleRenderingAfterSeek();if(resume&&video.paused)resumeVideo(video,true,playerState.streamToken);else playerState.pauseGuard=false;});
  document.addEventListener("keydown",event=>{if(event.key==="Escape"&&!document.fullscreenElement&&!panel.classList.contains("full-window"))closePlayer();else if(event.key==="Escape"&&panel.classList.contains("full-window"))setFullWindow(false);});
  document.addEventListener("keydown",handlePlayerKeyboardShortcut,true);
  panel.addEventListener("click",event=>{if(event.target===panel)closePlayer();});
})();
