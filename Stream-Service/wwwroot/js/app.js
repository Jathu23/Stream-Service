/**
 * FM Stream Player - Spotify-Style MP3 Player
 * Modern UI with Album Art and Progress Bar
 */

// Configuration
const CONFIG = {
    API_BASE: '/api/stream',
    AUTO_REFRESH: 10000,
    STATIONS: {} // Will be loaded dynamically from API
};

// State
const state = {
    currentStation: null,
    isPlaying: false,
    refreshTimer: null,
    progressTimer: null,
    bufferDuration: 0,
    currentTime: 0,
    seekPosition: 0,
    isDragging: false,
    isLive: true,
    playbackStartTime: null,
    seekedSecondsFromLive: 0,  // How many seconds from live when we seeked
    maxBufferDuration: 3600    // Maximum buffer duration in seconds (will be set per station)
};

// DOM Elements
const elements = {
    audio: null,
    albumArt: null,
    nowPlaying: null,
    nowPlayingStation: null,
    playButton: null,
    currentTime: null,
    duration: null,
    progressBar: null,
    progressFill: null,
    progressHandle: null,
    progressTooltip: null,
    liveIndicator: null,
    btnGoLive: null,
    statusGrid: null,
    stationBtns: null,
    canvas: null,
    canvasCtx: null,
    fullscreenCanvas: null,
    fullscreenCanvasCtx: null,
    fullscreenVisualizer: null,
    fullscreenStation: null,
    fullscreenTitle: null
};

// Web Audio API
let audioContext = null;
let analyser = null;
let source = null;
let dataArray = null;
let bufferLength = 0;
let animationId = null;
let fullscreenAnimationId = null;

// UI State
let isMinimalMode = false;
let isFullscreenVisualizer = false;

// Initialize
document.addEventListener('DOMContentLoaded', async () => {
    elements.audio = document.getElementById('audioPlayer');
    elements.albumArt = document.getElementById('albumArt');
    elements.nowPlaying = document.getElementById('nowPlayingTitle');
    elements.nowPlayingStation = document.getElementById('nowPlayingStation');
    elements.playButton = document.getElementById('playButton');
    elements.currentTime = document.getElementById('currentTime');
    elements.duration = document.getElementById('duration');
    elements.progressBar = document.getElementById('progressBar');
    elements.progressFill = document.getElementById('progressFill');
    elements.progressHandle = document.getElementById('progressHandle');
    elements.progressTooltip = document.getElementById('progressTooltip');
    elements.liveIndicator = document.getElementById('liveIndicator');
    elements.btnGoLive = document.getElementById('btnGoLive');
    elements.statusGrid = document.getElementById('statusGrid');
    elements.canvas = document.getElementById('visualizerCanvas');
    elements.canvasCtx = elements.canvas.getContext('2d');
    elements.fullscreenCanvas = document.getElementById('fullscreenCanvas');
    elements.fullscreenCanvasCtx = elements.fullscreenCanvas ? elements.fullscreenCanvas.getContext('2d') : null;
    elements.fullscreenVisualizer = document.getElementById('fullscreenVisualizer');
    elements.fullscreenStation = document.getElementById('fullscreenStation');
    elements.fullscreenTitle = document.getElementById('fullscreenTitle');
    elements.toastContainer = document.getElementById('toastContainer');
    elements.helpModal = document.getElementById('helpModal');

    // Set canvas size dynamically based on screen size
    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);

    // Note: Audio context will be initialized on first play (browser requirement)

    // Load stations from API
    await loadStations();

    setupListeners();
    setupSeekbar();

    // Auto-select first station by default
    const firstStationId = Object.keys(CONFIG.STATIONS)[0];
    if (firstStationId) {
        selectStation(firstStationId);
    }

    console.log('🎵 FM Stream Player ready');
});

// Load stations from API
async function loadStations() {
    try {
        const response = await fetch(`${CONFIG.API_BASE}/stations`);
        if (!response.ok) throw new Error('Failed to load stations');

        const stations = await response.json();

        // Convert array to object for easy lookup
        stations.forEach(station => {
            CONFIG.STATIONS[station.id] = {
                name: station.name,
                icon: station.icon,
                gradient: station.gradient,
                recordingHours: station.recordingHours || 1  // Default to 1 hour if not specified
            };
        });

        // Render station buttons
        renderStationButtons(stations);

        console.log('📡 Loaded stations:', Object.keys(CONFIG.STATIONS));
    } catch (error) {
        console.error('Error loading stations:', error);
        alert('Failed to load stations. Please refresh the page.');
    }
}

// Render station buttons dynamically
function renderStationButtons(stations) {
    const stationsList = document.getElementById('stationsList');
    if (!stationsList) return;

    // Mobile-first design with station cards
    stationsList.innerHTML = stations.map(station => `
        <button class="station-card" data-station="${station.id}">
            <div class="station-icon">${station.icon}</div>
            <div class="station-info">
                <div class="station-name">${station.name}</div>
                <div class="station-meta">${station.recordingHours}h buffer • Live FM</div>
            </div>
        </button>
    `).join('');

    // Setup event listeners for station buttons
    elements.stationBtns = document.querySelectorAll('.station-card');
    elements.stationBtns.forEach(btn => {
        btn.addEventListener('click', () => selectStation(btn.dataset.station));
    });
}

function setupListeners() {
    // Audio events
    elements.audio.addEventListener('play', () => {
        state.isPlaying = true;
        startProgressAnimation();
        startVisualizer();
    });

    elements.audio.addEventListener('pause', () => {
        state.isPlaying = false;
        stopProgressAnimation();
        stopVisualizer();
    });

    elements.audio.addEventListener('error', (e) => {
        console.error('Audio error:', e);
        alert('Playback error occurred');
    });

    // Keyboard shortcuts
    document.addEventListener('keydown', (e) => {
        if (e.code === 'Space' && state.currentStation) {
            e.preventDefault();
            state.isPlaying ? pauseStream() : playLive();
        }

        // Dynamic keyboard shortcuts based on loaded stations
        const stationIds = Object.keys(CONFIG.STATIONS);
        if (e.code === 'Digit1' && stationIds[0]) selectStation(stationIds[0]);
        if (e.code === 'Digit2' && stationIds[1]) selectStation(stationIds[1]);
        if (e.code === 'Digit3' && stationIds[2]) selectStation(stationIds[2]);

        // Previous/Next station shortcuts
        if (e.code === 'ArrowLeft' && state.currentStation) {
            e.preventDefault();
            previousStation();
        }
        if (e.code === 'ArrowRight' && state.currentStation) {
            e.preventDefault();
            nextStation();
        }

        if (e.code === 'KeyR' && state.currentStation) refreshStatus();
    });
}

// Station Management
function selectStation(stationId) {
    if (!CONFIG.STATIONS[stationId]) return;

    state.currentStation = stationId;
    const station = CONFIG.STATIONS[stationId];

    // Set maximum buffer duration for this station
    state.maxBufferDuration = station.recordingHours * 3600; // Convert hours to seconds

    console.log('📡 Station:', station.name, '| Max buffer:', station.recordingHours, 'hours (', state.maxBufferDuration, 'seconds)');

    // Update station buttons
    elements.stationBtns.forEach(btn => {
        btn.classList.toggle('active', btn.dataset.station === stationId);
    });

    // Update album art
    elements.albumArt.textContent = station.icon;
    elements.albumArt.style.background = station.gradient;

    // Update now playing
    elements.nowPlaying.textContent = station.name;
    elements.nowPlayingStation.textContent = 'Ready to play';

    // Show toast notification
    showToast(`📻 ${station.name} - ${station.recordingHours}h buffer`, 'success');

    // Refresh status
    refreshStatus();
    startAutoRefresh();

    console.log('✅ Selected:', station.name);
}

// Previous station
function previousStation() {
    const stationIds = Object.keys(CONFIG.STATIONS);
    if (stationIds.length === 0) return;

    const currentIndex = stationIds.indexOf(state.currentStation);
    const previousIndex = currentIndex <= 0 ? stationIds.length - 1 : currentIndex - 1;

    selectStation(stationIds[previousIndex]);

    // If currently playing, start playing the new station
    if (state.isPlaying) {
        playLive();
    }
}

// Next station
function nextStation() {
    const stationIds = Object.keys(CONFIG.STATIONS);
    if (stationIds.length === 0) return;

    const currentIndex = stationIds.indexOf(state.currentStation);
    const nextIndex = currentIndex >= stationIds.length - 1 ? 0 : currentIndex + 1;

    selectStation(stationIds[nextIndex]);

    // If currently playing, start playing the new station
    if (state.isPlaying) {
        playLive();
    }
}

// Playback Controls
function playLive() {
    if (!state.currentStation) {
        showToast('⚠️ Please select a station first!', 'warning');
        return;
    }

    const station = CONFIG.STATIONS[state.currentStation];
    const url = `${CONFIG.API_BASE}/live/${state.currentStation}`;

    // Initialize audio context on first play (required by browsers)
    if (!audioContext) {
        initAudioContext();
    }

    // Reset to live mode
    state.isLive = true;
    state.currentTime = state.bufferDuration;
    state.playbackStartTime = Date.now();

    elements.audio.src = url;
    elements.audio.play()
        .then(() => {
            state.isPlaying = true;

            // Update play button
            elements.playButton.textContent = '⏸️';
            elements.playButton.onclick = pauseStream;

            // Update now playing
            elements.nowPlaying.textContent = station.name;
            elements.nowPlayingStation.innerHTML = '🔴 LIVE <span class="playing-animation"><span></span><span></span><span></span></span>';

            // Show live indicator
            if (elements.liveIndicator) {
                elements.liveIndicator.style.display = 'inline-block';
            }

            // Hide album overlay - show playing
            if (elements.albumArt) {
                elements.albumArt.classList.add('playing');
            }

            // Show success toast
            showToast(`▶️ ${station.name} LIVE`, 'success');

            console.log('▶️ Playing:', url);
        })
        .catch(err => {
            console.error('Play error:', err);
            showToast('❌ Failed to play - Check connection', 'error');
        });
}

function pauseStream() {
    elements.audio.pause();
    state.isPlaying = false;

    // Update play button
    elements.playButton.textContent = '▶️';
    elements.playButton.onclick = playLive;

    elements.nowPlayingStation.textContent = 'Paused';

    // Show album overlay when paused
    if (elements.albumArt) {
        elements.albumArt.classList.remove('playing');
    }

    // Show toast
    showToast('⏸️ Paused', 'info');
}

function skipForward() {
    if (!state.currentStation) {
        showToast('⚠️ Select a station first', 'warning');
        return;
    }
    // Skip forward means go back to live
    showToast('⏩ Going LIVE', 'info');
    playLive();
}

function rewind(seconds) {
    if (!state.currentStation) {
        showToast('⚠️ Select a station first', 'warning');
        return;
    }

    const minutes = Math.floor(seconds / 60);
    const timeText = minutes > 0 ? `${minutes}m` : `${seconds}s`;
    showToast(`⏪ ${timeText} ago`, 'info');

    const station = CONFIG.STATIONS[state.currentStation];
    const url = `${CONFIG.API_BASE}/rewind/${state.currentStation}?seconds=${seconds}`;

    elements.audio.src = url;
    elements.audio.play()
        .then(() => {
            // Update play button
            elements.playButton.textContent = '⏸️';
            elements.playButton.onclick = pauseStream;

            // Update now playing
            elements.nowPlaying.textContent = station.name;
            elements.nowPlayingStation.innerHTML = `⏪ ${minutes} min ago <span class="playing-animation"><span></span><span></span><span></span></span>`;

            console.log('⏪ Rewinding:', url);
        })
        .catch(err => {
            console.error('Rewind error:', err);
            alert('Failed to rewind');
        });
}

function stopStream() {
    elements.audio.pause();
    elements.audio.src = '';
    state.isPlaying = false;

    // Update play button
    elements.playButton.textContent = '▶️';
    elements.playButton.onclick = playLive;

    // Update UI
    if (state.currentStation) {
        const station = CONFIG.STATIONS[state.currentStation];
        elements.nowPlaying.textContent = station.name;
        elements.nowPlayingStation.textContent = 'Stopped';
    } else {
        elements.nowPlaying.textContent = 'Select a station to start';
        elements.nowPlayingStation.textContent = '';
    }

    // Reset progress
    elements.currentTime.textContent = '0:00';
    elements.progressFill.style.width = '0%';
    stopProgressAnimation();

    console.log('⏹️ Stopped');
}

// Progress Bar Animation
function startProgressAnimation() {
    if (state.progressTimer) clearInterval(state.progressTimer);

    state.progressTimer = setInterval(() => {
        if (!state.isPlaying) {
            clearInterval(state.progressTimer);
            return;
        }

        updateSeekbarPosition();
    }, 1000);
}

function stopProgressAnimation() {
    if (state.progressTimer) {
        clearInterval(state.progressTimer);
        state.progressTimer = null;
    }
}

function updateDuration() {
    if (state.bufferDuration > 0) {
        // Use effective buffer duration (capped by station's recording hours)
        const effectiveBufferDuration = Math.min(state.bufferDuration, state.maxBufferDuration);

        const minutes = Math.floor(effectiveBufferDuration / 60);
        const seconds = Math.floor(effectiveBufferDuration % 60);
        elements.duration.textContent = `${minutes}:${seconds.toString().padStart(2, '0')}`;

        // Update seekbar max position
        if (elements.progressBar) {
            elements.progressBar.setAttribute('data-duration', effectiveBufferDuration);
        }

        // Log buffer info
        const station = state.currentStation ? CONFIG.STATIONS[state.currentStation] : null;
        const maxHours = station ? station.recordingHours : 1;
        const actualMinutes = Math.floor(state.bufferDuration / 60);
        console.log(`📊 Effective buffer: ${minutes}m ${seconds}s | Actual buffer: ${actualMinutes}m | Station max: ${maxHours}h`);
    } else {
        elements.duration.textContent = '--:--';
    }
}

// Status Management
async function refreshStatus() {
    if (!state.currentStation) {
        alert('Please select a station first!');
        return;
    }

    try {
        showLoading();

        const response = await fetch(`${CONFIG.API_BASE}/status/${state.currentStation}`);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        const data = await response.json();

        // Store buffer duration
        if (data.bufferDurationMinutes) {
            state.bufferDuration = data.bufferDurationMinutes * 60;
            updateDuration();
        }

        displayStatus(data);
        console.log('📊 Status refreshed');

    } catch (error) {
        console.error('Status error:', error);
        showError(error.message);
    }
}

function displayStatus(data) {
    const html = `
        <div class="status-item">
            <div class="status-label">Recording</div>
            <div class="status-value">
                ${data.isRecording ? '✓ Active' : '✗ Inactive'}
            </div>
        </div>
        <div class="status-item">
            <div class="status-label">Buffer Duration</div>
            <div class="status-value">${data.bufferDurationMinutes || 0} min</div>
        </div>
        <div class="status-item">
            <div class="status-label">Total Data</div>
            <div class="status-value">${data.totalMB || 0} MB</div>
        </div>
        <div class="status-item">
            <div class="status-label">Chunks</div>
            <div class="status-value">${data.chunkCount || 0}</div>
        </div>
        <div class="status-item">
            <div class="status-label">Oldest</div>
            <div class="status-value" style="font-size: 0.95rem;">
                ${data.oldestChunkTime ? formatTime(data.oldestChunkTime) : 'N/A'}
            </div>
        </div>
        <div class="status-item">
            <div class="status-label">Latest</div>
            <div class="status-value" style="font-size: 0.95rem;">
                ${data.latestChunkTime ? formatTime(data.latestChunkTime) : 'N/A'}
            </div>
        </div>
    `;

    elements.statusGrid.innerHTML = html;
}

function showLoading() {
    elements.statusGrid.innerHTML = `
        <div class="status-item">
            <div class="status-label">Status</div>
            <div class="status-value">🔄 Loading...</div>
        </div>
    `;
}

function showError(msg) {
    elements.statusGrid.innerHTML = `
        <div class="status-item">
            <div class="status-label">Error</div>
            <div class="status-value">✗ ${msg || 'Cannot connect'}</div>
        </div>
    `;
}

// Auto-Refresh
function startAutoRefresh() {
    if (state.refreshTimer) clearInterval(state.refreshTimer);

    state.refreshTimer = setInterval(() => {
        if (state.currentStation) refreshStatus();
    }, CONFIG.AUTO_REFRESH);

    console.log('🔄 Auto-refresh started');
}

// Web Audio API Initialization
function initAudioContext() {
    if (audioContext) return; // Already initialized

    try {
        audioContext = new (window.AudioContext || window.webkitAudioContext)();
        analyser = audioContext.createAnalyser();
        analyser.fftSize = 256; // Higher = more detail, lower = better performance
        analyser.smoothingTimeConstant = 0.8; // Smooth transitions
        bufferLength = analyser.frequencyBinCount;
        dataArray = new Uint8Array(bufferLength);

        // Connect audio element to analyser
        source = audioContext.createMediaElementSource(elements.audio);
        source.connect(analyser);
        analyser.connect(audioContext.destination);

        console.log('✅ Web Audio API initialized with real-time frequency analysis');
    } catch (error) {
        console.error('Failed to initialize Web Audio API:', error);
        alert('Your browser may not support audio visualization');
    }
}

// Resize Canvas for Responsive Design
function resizeCanvas() {
    if (!elements.canvas) return;

    const container = elements.canvas.parentElement;
    const containerWidth = container.clientWidth;

    // Set canvas dimensions based on screen size
    if (window.innerWidth <= 480) {
        // Mobile phones
        elements.canvas.width = Math.min(containerWidth - 40, 600);
        elements.canvas.height = 60;
    } else if (window.innerWidth <= 768) {
        // Tablets
        elements.canvas.width = Math.min(containerWidth - 40, 700);
        elements.canvas.height = 80;
    } else {
        // Desktop
        elements.canvas.width = Math.min(containerWidth - 40, 800);
        elements.canvas.height = 120;
    }
}

// Visualizer Controls
function startVisualizer() {
    if (!elements.canvas) return;

    // Resume audio context if suspended
    if (audioContext && audioContext.state === 'suspended') {
        audioContext.resume();
    }

    elements.canvas.classList.add('active');
    drawVisualizer();
}

function stopVisualizer() {
    if (!elements.canvas) return;

    elements.canvas.classList.remove('active');

    if (animationId) {
        cancelAnimationFrame(animationId);
        animationId = null;
    }

    // Clear canvas
    const ctx = elements.canvasCtx;
    ctx.clearRect(0, 0, elements.canvas.width, elements.canvas.height);
}

// Draw Visualizer - Multiple Professional Styles
function drawVisualizer() {
    if (!state.isPlaying) {
        stopVisualizer();
        return;
    }

    animationId = requestAnimationFrame(drawVisualizer);

    // Get frequency data
    analyser.getByteFrequencyData(dataArray);

    const canvas = elements.canvas;
    const ctx = elements.canvasCtx;
    const width = canvas.width;
    const height = canvas.height;

    // Clear canvas
    ctx.clearRect(0, 0, width, height);

    // Style 1: Horizontal Wave Bars (Like your image)
    drawSymmetricBars(ctx, width, height, dataArray, bufferLength);
}

// Style 1: Horizontal Wave Bars (Like your image)
function drawSymmetricBars(ctx, width, height, dataArray, bufferLength) {
    // Adjust number of bars based on screen size for better performance
    let numBars;
    if (window.innerWidth <= 480) {
        numBars = 50; // Mobile: fewer bars for performance
    } else if (window.innerWidth <= 768) {
        numBars = 65; // Tablet: medium bars
    } else {
        numBars = 80; // Desktop: full bars
    }

    const barWidth = width / numBars;
    const maxBarHeight = height * 0.45;

    for (let i = 0; i < numBars; i++) {
        // Map bar index to frequency data with interpolation
        const dataIndex = Math.floor((i / numBars) * bufferLength);
        const nextDataIndex = Math.min(dataIndex + 1, bufferLength - 1);
        const fraction = ((i / numBars) * bufferLength) - dataIndex;

        // Interpolate between adjacent frequency values for smoothness
        const value1 = dataArray[dataIndex] / 255;
        const value2 = dataArray[nextDataIndex] / 255;
        const interpolatedValue = value1 + (value2 - value1) * fraction;

        // Calculate bar height with some minimum height
        const barHeight = Math.max(4, interpolatedValue * maxBarHeight);

        // Position from center
        const centerY = height / 2;
        const x = i * barWidth;

        // Create gradient for each bar (red/pink like your image)
        const gradient = ctx.createLinearGradient(0, centerY - barHeight, 0, centerY + barHeight);
        gradient.addColorStop(0, '#ff1744');
        gradient.addColorStop(0.5, '#ff4569');
        gradient.addColorStop(1, '#ff1744');

        ctx.fillStyle = gradient;

        // Draw bar with rounded corners
        const barSpacing = 1;
        const barActualWidth = barWidth - barSpacing;

        // Draw top half
        ctx.fillRect(x, centerY - barHeight, barActualWidth, barHeight);

        // Draw bottom half (mirrored)
        ctx.fillRect(x, centerY, barActualWidth, barHeight);

        // Add subtle glow
        ctx.shadowBlur = 8;
        ctx.shadowColor = '#ff1744';
    }

    // Reset shadow
    ctx.shadowBlur = 0;
}

// Style 2: Circular Visualizer (Alternative)
function drawCircularVisualizer(ctx, width, height, dataArray, bufferLength) {
    const centerX = width / 2;
    const centerY = height / 2;
    const radius = Math.min(width, height) / 3;

    for (let i = 0; i < bufferLength; i++) {
        const angle = (i / bufferLength) * Math.PI * 2;
        const barHeight = (dataArray[i] / 255) * 50;

        const x1 = centerX + Math.cos(angle) * radius;
        const y1 = centerY + Math.sin(angle) * radius;
        const x2 = centerX + Math.cos(angle) * (radius + barHeight);
        const y2 = centerY + Math.sin(angle) * (radius + barHeight);

        // Gradient for each bar
        const gradient = ctx.createLinearGradient(x1, y1, x2, y2);
        gradient.addColorStop(0, '#1ed760');
        gradient.addColorStop(1, '#169c46');

        ctx.strokeStyle = gradient;
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.lineTo(x2, y2);
        ctx.stroke();
    }
}

// Style 3: Wave Visualizer
function drawWaveVisualizer(ctx, width, height, dataArray, bufferLength) {
    ctx.lineWidth = 3;
    ctx.strokeStyle = '#1db954';
    ctx.beginPath();

    const sliceWidth = width / bufferLength;
    let x = 0;

    for (let i = 0; i < bufferLength; i++) {
        const v = dataArray[i] / 255.0;
        const y = v * height;

        if (i === 0) {
            ctx.moveTo(x, y);
        } else {
            ctx.lineTo(x, y);
        }

        x += sliceWidth;
    }

    ctx.stroke();

    // Add glow
    ctx.shadowBlur = 15;
    ctx.shadowColor = '#1db954';
    ctx.stroke();
    ctx.shadowBlur = 0;
}

// Utilities
function formatTime(iso) {
    try {
        return new Date(iso).toLocaleTimeString('en-US', {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
    } catch {
        return 'Invalid';
    }
}

// Seekbar Setup and Handlers
function setupSeekbar() {
    if (!elements.progressBar) return;

    let isDragging = false;

    // Mouse/Touch drag handlers
    elements.progressBar.addEventListener('mousedown', startDrag);
    elements.progressBar.addEventListener('touchstart', startDrag);

    document.addEventListener('mousemove', drag);
    document.addEventListener('touchmove', drag);

    document.addEventListener('mouseup', endDrag);
    document.addEventListener('touchend', endDrag);

    function startDrag(e) {
        isDragging = true;
        state.isDragging = true;
        elements.progressHandle.classList.add('dragging');
        updateSeekPosition(e);
    }

    function drag(e) {
        if (!isDragging) return;
        updateSeekPosition(e);
    }

    function endDrag() {
        if (!isDragging) return;
        isDragging = false;
        state.isDragging = false;
        elements.progressHandle.classList.remove('dragging');

        // Seek to the position
        seekToPosition(state.seekPosition);
    }

    function updateSeekPosition(e) {
        const rect = elements.progressBar.getBoundingClientRect();
        const x = (e.type.includes('touch') ? e.touches[0].clientX : e.clientX) - rect.left;
        const percent = Math.max(0, Math.min(1, x / rect.width));

        // Use the SMALLER of bufferDuration and maxBufferDuration
        // This ensures seekbar doesn't go beyond station's recording limit
        const effectiveBufferDuration = Math.min(state.bufferDuration, state.maxBufferDuration);

        // Calculate position in buffer (0 = oldest, effectiveBufferDuration = live)
        state.seekPosition = percent * effectiveBufferDuration;

        // Update UI
        const position = percent * 100;
        elements.progressFill.style.width = `${position}%`;
        elements.progressHandle.style.left = `${position}%`;

        // Update tooltip - show how far from live
        const secondsFromLive = effectiveBufferDuration - state.seekPosition;
        const isAtLive = secondsFromLive < 5;

        // Show tooltip
        if (elements.progressTooltip) {
            elements.progressTooltip.style.display = 'block';

            if (isAtLive) {
                elements.progressTooltip.textContent = 'LIVE';
            } else {
                const minutesAgo = Math.floor(secondsFromLive / 60);
                const secondsAgo = Math.floor(secondsFromLive % 60);
                if (minutesAgo > 0) {
                    elements.progressTooltip.textContent = `${minutesAgo}:${secondsAgo.toString().padStart(2, '0')} ago`;
                } else {
                    elements.progressTooltip.textContent = `${secondsAgo}s ago`;
                }
            }
        }

        console.log('🎯 Seek position:', formatTimeFromSeconds(state.seekPosition), '| From live:', Math.floor(secondsFromLive) + 's');
    }

    // Click to seek
    elements.progressBar.addEventListener('click', (e) => {
        if (!state.isDragging) {
            const rect = elements.progressBar.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const percent = Math.max(0, Math.min(1, x / rect.width));

            // Use effective buffer duration (limited by station's max)
            const effectiveBufferDuration = Math.min(state.bufferDuration, state.maxBufferDuration);
            state.seekPosition = percent * effectiveBufferDuration;

            seekToPosition(state.seekPosition);
        }
    });
}

// Seek to specific position
function seekToPosition(seconds) {
    if (!state.currentStation) {
        console.warn('No station selected');
        return;
    }

    if (state.bufferDuration === 0) {
        console.warn('Buffer not ready yet');
        return;
    }

    // Use effective buffer duration (limited by station's recording hours)
    const effectiveBufferDuration = Math.min(state.bufferDuration, state.maxBufferDuration);

    // Validate seek position (seconds is 0 to effectiveBufferDuration)
    if (seconds < 0 || seconds > effectiveBufferDuration) {
        console.error('❌ Invalid seek position:', seconds, '| Max allowed:', effectiveBufferDuration);
        elements.nowPlayingStation.textContent = '⚠️ Cannot seek to that position';
        setTimeout(() => {
            if (state.isPlaying) {
                const station = CONFIG.STATIONS[state.currentStation];
                elements.nowPlayingStation.textContent = station.name;
            }
        }, 2000);
        return;
    }

    // Calculate how far back from live we want to go
    // If seconds = effectiveBufferDuration (right edge), we're at LIVE
    // If seconds = 0 (left edge), we're at the oldest buffered time within the limit
    const secondsFromLive = effectiveBufferDuration - seconds;

    // Additional safety check: don't seek beyond actual available buffer
    if (secondsFromLive > state.bufferDuration) {
        console.warn('⚠️ Requested position beyond available buffer');
        console.log('Requested seconds from live:', secondsFromLive, '| Available buffer:', state.bufferDuration);
        // Adjust to available buffer
        const adjustedSecondsFromLive = Math.min(secondsFromLive, state.bufferDuration);
        console.log('Adjusting to:', adjustedSecondsFromLive, 'seconds from live');
    }

    console.log('🎯 Seek request - Position:', formatTimeFromSeconds(seconds),
                '| Seconds from live:', secondsFromLive,
                '| Effective buffer:', formatTimeFromSeconds(effectiveBufferDuration));

    // If within 5 seconds of live, just go to live
    if (secondsFromLive < 5) {
        goToLive();
        return;
    }

    // Play from that position
    state.isLive = false;
    if (elements.liveIndicator) {
        elements.liveIndicator.style.display = 'none';
    }

    // Store how far from live we are (this is what matters!)
    state.seekedSecondsFromLive = secondsFromLive;
    state.playbackStartTime = Date.now();

    // Also store the position for reference
    state.currentTime = seconds;

    const station = CONFIG.STATIONS[state.currentStation];
    const rewindSeconds = Math.floor(secondsFromLive);

    // Validate rewind seconds is within station's max buffer duration
    if (rewindSeconds < 0 || rewindSeconds > state.maxBufferDuration) {
        console.error('Invalid rewind seconds:', rewindSeconds, '| Max allowed:', state.maxBufferDuration);
        return;
    }

    const url = `${CONFIG.API_BASE}/rewind/${state.currentStation}?seconds=${rewindSeconds}`;

    console.log('🎯 Seeking to:', formatTimeFromSeconds(seconds), `(${rewindSeconds}s from live)`);

    // Initialize audio context on first play
    if (!audioContext) {
        initAudioContext();
    }

    // Validate URL before loading
    console.log('📡 Loading stream from:', url);

    elements.audio.src = url;

    // Add load error handler
    const handleLoadError = () => {
        console.error('❌ Failed to load audio stream');
        state.isLive = true;
        elements.nowPlayingStation.textContent = '⚠️ Seeking failed, switching to live...';
        setTimeout(() => {
            playLive();
        }, 1000);
    };

    elements.audio.addEventListener('error', handleLoadError, { once: true });

    elements.audio.play()
        .then(() => {
            state.isPlaying = true;
            elements.playButton.textContent = '⏸️';
            elements.playButton.onclick = pauseStream;
            elements.nowPlaying.textContent = station.name;

            const minutesAgo = Math.floor(rewindSeconds / 60);
            const secondsAgo = rewindSeconds % 60;
            const timeText = minutesAgo > 0
                ? `${minutesAgo}:${secondsAgo.toString().padStart(2, '0')} ago`
                : `${secondsAgo}s ago`;

            elements.nowPlayingStation.innerHTML = `⏪ ${timeText} <span class="playing-animation"><span></span><span></span><span></span></span>`;

            console.log('✅ Seek successful - Now playing from', timeText);
        })
        .catch(err => {
            console.error('❌ Seek play error:', err.message || err);
            state.isLive = true;

            // Check if it's a network error or invalid seek position
            if (err.name === 'NotSupportedError' || err.name === 'NotAllowedError') {
                console.warn('⚠️ Audio playback not allowed or supported');
                elements.nowPlayingStation.textContent = '⚠️ Playback error - Click play to retry';
            } else {
                console.warn('⚠️ Falling back to live stream');
                elements.nowPlayingStation.textContent = '⚠️ Seek failed, switching to live...';
                setTimeout(() => playLive(), 1000);
            }
        });
}

// Go to Live function
function goToLive() {
    if (!state.currentStation) return;

    console.log('📡 Switching to LIVE');
    showToast('🔴 LIVE', 'success');
    playLive();
}

// Format seconds to MM:SS
function formatTimeFromSeconds(seconds) {
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs.toString().padStart(2, '0')}`;
}

// Update seekbar position during playback
function updateSeekbarPosition() {
    if (state.isDragging || !state.isPlaying) return;

    // Use effective buffer duration (limited by station's recording hours)
    const effectiveBufferDuration = Math.min(state.bufferDuration, state.maxBufferDuration);

    // Calculate current position in the buffer
    let currentPos;

    if (state.isLive) {
        // We're at the live position - always at the right edge
        // Use effective buffer duration (capped by recording hours)
        currentPos = effectiveBufferDuration;

        if (elements.liveIndicator) {
            elements.liveIndicator.style.display = 'inline-block';
        }
        if (elements.btnGoLive) {
            elements.btnGoLive.style.display = 'none';
        }
    } else {
        // We're in time-shift mode - we're playing from X seconds ago
        // The KEY insight: When we rewound to "30 seconds ago", the backend
        // gave us a stream starting from that point. As we play, the backend
        // stream continues forward. Meanwhile, LIVE also continues forward.
        // So we STAY at "30 seconds from live" - we don't catch up automatically!

        // When we seeked, we stored how far from live we were
        // We stay at that distance because both playback and live advance together
        const currentSecondsFromLive = state.seekedSecondsFromLive;

        // Our position in the buffer is: effective buffer end - distance from live
        currentPos = effectiveBufferDuration - currentSecondsFromLive;

        // Make sure we don't go negative or beyond effective buffer
        currentPos = Math.max(0, Math.min(currentPos, effectiveBufferDuration));

        // Calculate how far we are from live
        const secondsFromLive = currentSecondsFromLive;

        // Show "Go to Live" button since we're not at live
        if (elements.liveIndicator) {
            elements.liveIndicator.style.display = 'none';
        }
        if (elements.btnGoLive) {
            elements.btnGoLive.style.display = 'inline-flex';
        }

        // Note: In this implementation, we DON'T automatically catch up to live
        // because the backend stream continues at the same pace as live
        // User must click "Go to Live" button or drag seekbar to catch up
        // This matches the user's request: "user seeker ah move panna avar move panna
        // time ku erra mathiri live la irunthu late ah audio keppar sudently jump akamaddar"
    }

    // Update UI with smooth transitions
    // Calculate percentage based on effective buffer duration
    const percent = effectiveBufferDuration > 0 ? (currentPos / effectiveBufferDuration) * 100 : 0;

    if (elements.progressFill) {
        elements.progressFill.style.width = `${percent}%`;
    }
    if (elements.progressHandle) {
        elements.progressHandle.style.left = `${percent}%`;
    }

    // Update current time display
    if (elements.currentTime) {
        const timeText = formatTimeFromSeconds(currentPos);
        elements.currentTime.textContent = timeText;
    }
}

// Minimal Mode Toggle
function toggleMinimalMode() {
    isMinimalMode = !isMinimalMode;
    document.body.classList.toggle('minimal-mode', isMinimalMode);

    const toggleBtn = document.getElementById('btnMinimalToggle');
    const icon = toggleBtn.querySelector('.toggle-icon');

    if (isMinimalMode) {
        icon.textContent = '🎛️';
        console.log('📱 Minimal mode enabled');
    } else {
        icon.textContent = '🎵';
        console.log('🖥️ Full mode enabled');
    }
}

// Fullscreen Visualizer Functions
function toggleFullscreenVisualizer() {
    if (!state.isPlaying) {
        alert('Please start playing a station first!');
        return;
    }

    if (isFullscreenVisualizer) {
        closeFullscreenVisualizer();
    } else {
        openFullscreenVisualizer();
    }
}

function openFullscreenVisualizer() {
    isFullscreenVisualizer = true;

    if (elements.fullscreenVisualizer) {
        elements.fullscreenVisualizer.style.display = 'flex';

        // Update info
        if (state.currentStation && CONFIG.STATIONS[state.currentStation]) {
            const station = CONFIG.STATIONS[state.currentStation];
            elements.fullscreenStation.textContent = station.name;
            elements.fullscreenTitle.textContent = state.isLive ? 'LIVE' : 'Time-Shift Mode';
        }

        // Resize fullscreen canvas
        resizeFullscreenCanvas();

        // Start fullscreen visualizer animation
        drawFullscreenVisualizer();

        console.log('🎨 Fullscreen visualizer opened');
    }
}

function closeFullscreenVisualizer() {
    isFullscreenVisualizer = false;

    if (elements.fullscreenVisualizer) {
        elements.fullscreenVisualizer.style.display = 'none';
    }

    // Stop fullscreen animation
    if (fullscreenAnimationId) {
        cancelAnimationFrame(fullscreenAnimationId);
        fullscreenAnimationId = null;
    }

    console.log('❌ Fullscreen visualizer closed');
}

// Resize Fullscreen Canvas
function resizeFullscreenCanvas() {
    if (!elements.fullscreenCanvas) return;

    const width = Math.min(window.innerWidth * 0.9, 1400);
    const height = window.innerHeight * 0.6;

    elements.fullscreenCanvas.width = width;
    elements.fullscreenCanvas.height = height;
}

// Fullscreen Visualizer Animation
function drawFullscreenVisualizer() {
    if (!isFullscreenVisualizer || !state.isPlaying) {
        if (fullscreenAnimationId) {
            cancelAnimationFrame(fullscreenAnimationId);
            fullscreenAnimationId = null;
        }
        return;
    }

    fullscreenAnimationId = requestAnimationFrame(drawFullscreenVisualizer);

    if (!analyser || !dataArray) return;

    // Get frequency data
    analyser.getByteFrequencyData(dataArray);

    const canvas = elements.fullscreenCanvas;
    const ctx = elements.fullscreenCanvasCtx;
    const width = canvas.width;
    const height = canvas.height;

    // Clear canvas with fade effect
    ctx.fillStyle = 'rgba(15, 12, 41, 0.2)';
    ctx.fillRect(0, 0, width, height);

    // Draw multiple visualizer styles for desktop
    drawFullscreenCircularWave(ctx, width, height, dataArray, bufferLength);
    drawFullscreenSymmetricBars(ctx, width, height, dataArray, bufferLength);
}

// Fullscreen Circular Wave Visualizer
function drawFullscreenCircularWave(ctx, width, height, dataArray, bufferLength) {
    const centerX = width / 2;
    const centerY = height / 2;
    const baseRadius = Math.min(width, height) / 4;
    const numPoints = 128;

    ctx.beginPath();
    ctx.strokeStyle = 'rgba(29, 185, 84, 0.8)';
    ctx.lineWidth = 3;

    for (let i = 0; i < numPoints; i++) {
        const angle = (i / numPoints) * Math.PI * 2;
        const dataIndex = Math.floor((i / numPoints) * bufferLength);
        const value = dataArray[dataIndex] / 255;
        const radius = baseRadius + value * 80;

        const x = centerX + Math.cos(angle) * radius;
        const y = centerY + Math.sin(angle) * radius;

        if (i === 0) {
            ctx.moveTo(x, y);
        } else {
            ctx.lineTo(x, y);
        }
    }

    ctx.closePath();
    ctx.stroke();

    // Add glow effect
    ctx.shadowBlur = 20;
    ctx.shadowColor = '#1db954';
    ctx.stroke();
    ctx.shadowBlur = 0;
}

// Fullscreen Symmetric Bars
function drawFullscreenSymmetricBars(ctx, width, height, dataArray, bufferLength) {
    const numBars = 150; // More bars for desktop
    const barWidth = width / numBars;
    const maxBarHeight = height * 0.4;

    for (let i = 0; i < numBars; i++) {
        const dataIndex = Math.floor((i / numBars) * bufferLength);
        const nextDataIndex = Math.min(dataIndex + 1, bufferLength - 1);
        const fraction = ((i / numBars) * bufferLength) - dataIndex;

        const value1 = dataArray[dataIndex] / 255;
        const value2 = dataArray[nextDataIndex] / 255;
        const interpolatedValue = value1 + (value2 - value1) * fraction;

        const barHeight = Math.max(4, interpolatedValue * maxBarHeight);

        const centerY = height / 2;
        const x = i * barWidth;

        // Rainbow gradient effect
        const hue = (i / numBars) * 360;
        const gradient = ctx.createLinearGradient(0, centerY - barHeight, 0, centerY + barHeight);
        gradient.addColorStop(0, `hsla(${hue}, 80%, 60%, 0.8)`);
        gradient.addColorStop(0.5, `hsla(${hue + 30}, 80%, 65%, 0.9)`);
        gradient.addColorStop(1, `hsla(${hue}, 80%, 60%, 0.8)`);

        ctx.fillStyle = gradient;

        const barSpacing = 2;
        const barActualWidth = barWidth - barSpacing;

        // Draw symmetric bars
        ctx.fillRect(x, centerY - barHeight, barActualWidth, barHeight);
        ctx.fillRect(x, centerY, barActualWidth, barHeight);

        // Add glow
        ctx.shadowBlur = 10;
        ctx.shadowColor = `hsla(${hue}, 80%, 60%, 0.6)`;
    }

    ctx.shadowBlur = 0;
}

// Help Modal Toggle
function toggleHelp() {
    const helpModal = elements.helpModal || document.getElementById('helpModal');
    if (helpModal) {
        const isVisible = helpModal.style.display !== 'none';
        helpModal.style.display = isVisible ? 'none' : 'flex';
    }
}

// Toast Notification System
function showToast(message, type = 'info') {
    if (!elements.toastContainer) return;

    const icons = {
        success: '✅',
        error: '❌',
        info: 'ℹ️',
        warning: '⚠️'
    };

    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.innerHTML = `
        <div class="toast-icon">${icons[type] || icons.info}</div>
        <div class="toast-content">
            <p class="toast-message">${message}</p>
        </div>
    `;

    elements.toastContainer.appendChild(toast);

    // Auto-remove after 4 seconds
    setTimeout(() => {
        toast.classList.add('hiding');
        setTimeout(() => {
            toast.remove();
        }, 300);
    }, 4000);
}

// Export functions for HTML onclick
window.playLive = playLive;
window.pauseStream = pauseStream;
window.skipForward = skipForward;
window.rewind = rewind;
window.stopStream = stopStream;
window.refreshStatus = refreshStatus;
window.previousStation = previousStation;
window.nextStation = nextStation;
window.goToLive = goToLive;
window.toggleMinimalMode = toggleMinimalMode;
window.toggleFullscreenVisualizer = toggleFullscreenVisualizer;
window.closeFullscreenVisualizer = closeFullscreenVisualizer;
window.toggleHelp = toggleHelp;
window.showToast = showToast;

console.log('✅ App loaded - Real-time audio visualizer ready');
