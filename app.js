'use strict';

// ── PDF.js worker ──────────────────────────────────────────────────────────
pdfjsLib.GlobalWorkerOptions.workerSrc =
    'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';

// ── State ─────────────────────────────────────────────────────────────────
const state = {
    apiKey: '',
    pdfFile: null,
    questions: [],
    currentIndex: 0,
    score: 0,
    answers: [],         // { correct: bool|null, userAnswer: any }
    awaitingSelfAssess: false,
};

// ── DOM refs ───────────────────────────────────────────────────────────────
const $ = id => document.getElementById(id);

const screens = {
    setup:   $('screen-setup'),
    loading: $('screen-loading'),
    quiz:    $('screen-quiz'),
    results: $('screen-results'),
};

// ── Screen navigation ──────────────────────────────────────────────────────
function showScreen(name) {
    Object.values(screens).forEach(s => s.classList.remove('active'));
    screens[name].classList.add('active');
    window.scrollTo(0, 0);
}

// ── Saved API key ──────────────────────────────────────────────────────────
(function restoreSavedKey() {
    const saved = localStorage.getItem('medquiz_api_key');
    if (saved) {
        $('api-key').value = saved;
        $('save-key').checked = true;
        updateStartBtn();
    }
})();

// ── API key visibility toggle ──────────────────────────────────────────────
$('toggle-api-key').addEventListener('click', () => {
    const input = $('api-key');
    input.type = input.type === 'password' ? 'text' : 'password';
});

$('api-key').addEventListener('input', () => {
    if ($('save-key').checked) {
        localStorage.setItem('medquiz_api_key', $('api-key').value.trim());
    }
    updateStartBtn();
});

$('save-key').addEventListener('change', () => {
    if ($('save-key').checked) {
        localStorage.setItem('medquiz_api_key', $('api-key').value.trim());
    } else {
        localStorage.removeItem('medquiz_api_key');
    }
});

// ── File upload ────────────────────────────────────────────────────────────
const uploadZone = $('upload-zone');
const fileInput  = $('file-input');

uploadZone.addEventListener('dragover', e => { e.preventDefault(); uploadZone.classList.add('drag-over'); });
uploadZone.addEventListener('dragleave', () => uploadZone.classList.remove('drag-over'));
uploadZone.addEventListener('drop', e => {
    e.preventDefault();
    uploadZone.classList.remove('drag-over');
    const file = e.dataTransfer.files[0];
    if (file && file.type === 'application/pdf') setFile(file);
});

fileInput.addEventListener('change', () => {
    if (fileInput.files[0]) setFile(fileInput.files[0]);
});

function setFile(file) {
    state.pdfFile = file;
    $('file-name').textContent = file.name;
    $('file-info').classList.remove('hidden');
    uploadZone.style.display = 'none';
    updateStartBtn();
}

$('remove-file').addEventListener('click', () => {
    state.pdfFile = null;
    fileInput.value = '';
    $('file-info').classList.add('hidden');
    uploadZone.style.display = '';
    updateStartBtn();
});

function updateStartBtn() {
    const hasKey  = $('api-key').value.trim().length > 10;
    const hasFile = !!state.pdfFile;
    $('start-btn').disabled = !(hasKey && hasFile);
}

// ── Start Quiz ─────────────────────────────────────────────────────────────
$('start-btn').addEventListener('click', startQuiz);

async function startQuiz() {
    const apiKey   = $('api-key').value.trim();
    const count    = parseInt($('question-count').value, 10);
    const diff     = $('difficulty').value;
    const focus    = $('focus-area').value.trim();
    const lang     = $('language').value;

    state.apiKey = apiKey;
    $('start-error').classList.add('hidden');

    showScreen('loading');
    setLoadingStatus('PDF wird eingelesen…', 10);

    let pdfText;
    try {
        pdfText = await extractPdfText(state.pdfFile);
    } catch (err) {
        showScreen('setup');
        showError('PDF konnte nicht gelesen werden: ' + err.message);
        return;
    }

    setLoadingStatus('KI analysiert das Dokument…', 40);

    let questions;
    try {
        questions = await generateQuestions(apiKey, pdfText, count, diff, focus, lang);
    } catch (err) {
        showScreen('setup');
        showError('Fehler beim Generieren der Fragen: ' + err.message);
        return;
    }

    setLoadingStatus('Quiz wird vorbereitet…', 90);

    state.questions   = questions;
    state.currentIndex = 0;
    state.score        = 0;
    state.answers      = [];

    setTimeout(() => {
        setLoadingStatus('Fertig!', 100);
        setTimeout(() => {
            showScreen('quiz');
            renderQuestion(0);
        }, 400);
    }, 300);
}

function showError(msg) {
    const el = $('start-error');
    el.textContent = msg;
    el.classList.remove('hidden');
}

function setLoadingStatus(text, pct) {
    $('loading-status').textContent   = text;
    $('loading-progress').style.width = pct + '%';
}

// ── PDF Text Extraction ────────────────────────────────────────────────────
async function extractPdfText(file) {
    const buffer   = await file.arrayBuffer();
    const pdf      = await pdfjsLib.getDocument({ data: buffer }).promise;
    const maxPages = Math.min(pdf.numPages, 40);
    const parts    = [];

    for (let i = 1; i <= maxPages; i++) {
        const page    = await pdf.getPage(i);
        const content = await page.getTextContent();
        const text    = content.items.map(item => item.str).join(' ');
        parts.push(text);
    }

    const fullText = parts.join('\n\n');
    // Limit to ~14 000 chars to stay within token budget
    return fullText.length > 14000 ? fullText.slice(0, 14000) + '\n[…Dokument gekürzt]' : fullText;
}

// ── Claude API ─────────────────────────────────────────────────────────────
async function generateQuestions(apiKey, text, count, difficulty, focus, language) {
    const focusNote = focus ? `Fokussiere dich dabei besonders auf das Thema: "${focus}".` : '';

    const prompt = `Du bist ein Lernassistent für medizinische Berufsausbildungen.
Analysiere den folgenden Text und erstelle exakt ${count} Prüfungsfragen auf dem Schwierigkeitsgrad "${difficulty}".
Sprache der Fragen und Antworten: ${language}.
${focusNote}

Erstelle eine ausgewogene Mischung aus diesen drei Typen:
1. "multiple_choice" – 4 Antwortoptionen, genau eine ist richtig
2. "true_false"      – eine Aussage, die wahr oder falsch ist
3. "short_answer"    – offene Frage mit einer Musterlösung

WICHTIG: Antworte AUSSCHLIESSLICH mit einem gültigen JSON-Array, ohne Kommentare oder Erklärungen davor/danach.
Format:

[
  {
    "type": "multiple_choice",
    "question": "Wie viele Kammern hat das menschliche Herz?",
    "options": ["Zwei", "Drei", "Vier", "Fünf"],
    "correct": 2,
    "explanation": "Das Herz besteht aus vier Kammern: zwei Vorhöfen und zwei Ventrikeln."
  },
  {
    "type": "true_false",
    "question": "Die Lunge gibt Kohlendioxid ab und nimmt Sauerstoff auf.",
    "correct": true,
    "explanation": "Im kleinen Kreislauf wird CO₂ abgeatmet und O₂ ins Blut aufgenommen."
  },
  {
    "type": "short_answer",
    "question": "Erkläre die Funktion der Herzklappen.",
    "answer": "Herzklappen verhindern den Rückfluss des Blutes und sorgen für einen gerichteten Blutfluss durch das Herz.",
    "keywords": ["Rückfluss", "Blutfluss", "Klappe"],
    "explanation": "Es gibt vier Herzklappen: Mitral-, Trikuspidal-, Aorten- und Pulmonalklappe."
  }
]

Text aus dem Dokument:
${text}`;

    const response = await fetch('https://api.anthropic.com/v1/messages', {
        method: 'POST',
        headers: {
            'Content-Type':         'application/json',
            'x-api-key':            apiKey,
            'anthropic-version':    '2023-06-01',
            'anthropic-dangerous-direct-browser-access': 'true',
        },
        body: JSON.stringify({
            model:      'claude-opus-4-7',
            max_tokens: 4096,
            messages: [{ role: 'user', content: prompt }],
        }),
    });

    if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error(err.error?.message || `HTTP ${response.status}`);
    }

    const data = await response.json();
    const raw  = data.content[0].text.trim();

    // Extract JSON array from response (handles markdown code blocks)
    const jsonMatch = raw.match(/\[[\s\S]*\]/);
    if (!jsonMatch) throw new Error('Die KI hat kein gültiges JSON zurückgegeben.');

    const questions = JSON.parse(jsonMatch[0]);
    if (!Array.isArray(questions) || questions.length === 0) {
        throw new Error('Keine Fragen erhalten.');
    }
    return questions;
}

// ── Quiz Rendering ─────────────────────────────────────────────────────────
function renderQuestion(index) {
    const q   = state.questions[index];
    const tot = state.questions.length;

    $('question-counter').textContent = `Frage ${index + 1} von ${tot}`;
    $('score-display').textContent    = `${state.score} Punkt${state.score !== 1 ? 'e' : ''}`;
    $('quiz-progress').style.width    = (index / tot * 100) + '%';
    $('question-text').textContent    = q.question;

    // Badge
    const badge = $('question-type-badge');
    if (q.type === 'multiple_choice') { badge.textContent = 'Multiple Choice'; badge.className = 'type-badge badge-mc'; }
    else if (q.type === 'true_false') { badge.textContent = 'Wahr / Falsch';   badge.className = 'type-badge badge-tf'; }
    else                              { badge.textContent = 'Kurzantwort';      badge.className = 'type-badge badge-short'; }

    // Answer area
    const area = $('answer-area');
    area.innerHTML = '';
    area.classList.remove('answered');

    if (q.type === 'multiple_choice') renderMC(q, area);
    else if (q.type === 'true_false') renderTF(q, area);
    else                              renderShort(q, area);

    // Feedback + buttons reset
    const fb = $('feedback-area');
    fb.innerHTML = '';
    fb.className = 'feedback-area hidden';

    $('submit-btn').classList.remove('hidden');
    $('submit-btn').disabled = true;
    $('next-btn').classList.add('hidden');

    state.awaitingSelfAssess = false;
}

function renderMC(q, area) {
    const letters = ['A', 'B', 'C', 'D'];
    q.options.forEach((opt, i) => {
        const div = document.createElement('div');
        div.className = 'mc-option';
        div.dataset.index = i;
        div.innerHTML = `<span class="option-letter">${letters[i]}</span><span class="option-text">${opt}</span>`;
        div.addEventListener('click', () => selectMC(div));
        area.appendChild(div);
    });
}

function selectMC(chosen) {
    document.querySelectorAll('.mc-option').forEach(o => o.classList.remove('selected'));
    chosen.classList.add('selected');
    $('submit-btn').disabled = false;
}

function renderTF(q, area) {
    const wrap = document.createElement('div');
    wrap.className = 'tf-buttons';

    ['Wahr', 'Falsch'].forEach(label => {
        const btn = document.createElement('button');
        btn.className = 'tf-btn';
        btn.dataset.value = label === 'Wahr' ? 'true' : 'false';
        btn.innerHTML = `<span class="tf-icon">${label === 'Wahr' ? '✓' : '✗'}</span>${label}`;
        btn.addEventListener('click', () => {
            document.querySelectorAll('.tf-btn').forEach(b => b.classList.remove('selected'));
            btn.classList.add('selected');
            $('submit-btn').disabled = false;
        });
        wrap.appendChild(btn);
    });
    area.appendChild(wrap);
}

function renderShort(q, area) {
    const wrap = document.createElement('div');
    wrap.className = 'short-answer-area';
    const ta = document.createElement('textarea');
    ta.placeholder = 'Deine Antwort…';
    ta.addEventListener('input', () => {
        $('submit-btn').disabled = ta.value.trim().length === 0;
    });
    wrap.appendChild(ta);
    area.appendChild(wrap);
}

// ── Submit Answer ──────────────────────────────────────────────────────────
$('submit-btn').addEventListener('click', () => {
    const q = state.questions[state.currentIndex];

    if (q.type === 'multiple_choice') submitMC(q);
    else if (q.type === 'true_false') submitTF(q);
    else                              submitShort(q);
});

function submitMC(q) {
    const selected = document.querySelector('.mc-option.selected');
    if (!selected) return;

    const chosen  = parseInt(selected.dataset.index, 10);
    const correct = chosen === q.correct;

    lockAnswers();

    document.querySelectorAll('.mc-option').forEach((opt, i) => {
        if (i === q.correct)  opt.classList.add('correct');
        else if (i === chosen) opt.classList.add('wrong');
    });

    recordAnswer(correct, q.options[chosen]);
    showFeedback(correct ? 'correct' : 'wrong', q.explanation);
}

function submitTF(q) {
    const selected = document.querySelector('.tf-btn.selected');
    if (!selected) return;

    const userVal  = selected.dataset.value === 'true';
    const correct  = userVal === q.correct;

    lockAnswers();

    document.querySelectorAll('.tf-btn').forEach(btn => {
        const isCorrect = (btn.dataset.value === 'true') === q.correct;
        if (isCorrect)                    btn.classList.add('correct');
        else if (btn.classList.contains('selected')) btn.classList.add('wrong');
    });

    recordAnswer(correct, userVal ? 'Wahr' : 'Falsch');
    showFeedback(correct ? 'correct' : 'wrong', q.explanation);
}

function submitShort(q) {
    const ta = document.querySelector('.short-answer-area textarea');
    if (!ta || ta.value.trim() === '') return;

    lockAnswers();
    ta.classList.add('answered');
    ta.readOnly = true;

    // Show expected answer + self-assessment
    state.awaitingSelfAssess = true;
    $('submit-btn').classList.add('hidden');
    showSelfAssessment(q, ta.value.trim());
}

function lockAnswers() {
    $('answer-area').classList.add('answered');
    $('submit-btn').classList.add('hidden');
}

function recordAnswer(correct, userAnswer) {
    if (correct) state.score++;
    state.answers.push({ correct, userAnswer });
    $('score-display').textContent = `${state.score} Punkt${state.score !== 1 ? 'e' : ''}`;
    $('next-btn').classList.remove('hidden');
}

// ── Feedback ───────────────────────────────────────────────────────────────
function showFeedback(type, explanation) {
    const fb = $('feedback-area');
    fb.className = `feedback-area ${type === 'correct' ? 'correct-fb' : 'wrong-fb'}`;
    fb.innerHTML = `
        <div class="feedback-header">
            ${type === 'correct' ? '✓ Richtig!' : '✗ Falsch!'}
        </div>
        <div class="feedback-explanation">${escHtml(explanation)}</div>
    `;
    fb.classList.remove('hidden');
}

function showSelfAssessment(q, userAnswer) {
    const fb = $('feedback-area');
    fb.className = 'feedback-area self-fb';
    fb.innerHTML = `
        <div class="feedback-header">Musterlösung</div>
        <div class="feedback-expected">
            <strong>Erwartete Antwort:</strong><br>${escHtml(q.answer)}
        </div>
        <div class="feedback-explanation">${escHtml(q.explanation)}</div>
        <p style="margin-top:12px;font-size:0.85rem;font-weight:600;color:var(--text-muted)">
            Hast du die wesentlichen Punkte getroffen?
        </p>
        <div class="self-assess">
            <button class="self-assess-btn yes" id="sa-yes">✓ Ja, war richtig</button>
            <button class="self-assess-btn no"  id="sa-no">✗ Nein, war falsch</button>
        </div>
    `;
    fb.classList.remove('hidden');

    $('sa-yes').addEventListener('click', () => {
        recordAnswer(true,  userAnswer);
        state.awaitingSelfAssess = false;
        $('sa-yes').disabled = true;
        $('sa-no').disabled  = true;
    });

    $('sa-no').addEventListener('click', () => {
        recordAnswer(false, userAnswer);
        state.awaitingSelfAssess = false;
        $('sa-yes').disabled = true;
        $('sa-no').disabled  = true;
    });
}

// ── Next Question ──────────────────────────────────────────────────────────
$('next-btn').addEventListener('click', () => {
    if (state.awaitingSelfAssess) return;

    const next = state.currentIndex + 1;
    if (next < state.questions.length) {
        state.currentIndex = next;
        renderQuestion(next);
    } else {
        showResults();
    }
});

// ── Results ────────────────────────────────────────────────────────────────
function showResults() {
    showScreen('results');

    const total   = state.questions.length;
    const correct = state.answers.filter(a => a.correct).length;
    const wrong   = total - correct;
    const pct     = Math.round((correct / total) * 100);

    $('score-percent').textContent = pct + '%';
    $('correct-count').textContent = correct;
    $('wrong-count').textContent   = wrong;
    $('total-count').textContent   = total;

    // Animate ring
    const circumference = 2 * Math.PI * 52; // r=52
    const offset        = circumference * (1 - pct / 100);
    const ring          = $('score-ring-fill');
    ring.style.strokeDasharray  = circumference;
    ring.style.strokeDashoffset = circumference;
    ring.style.transition       = 'stroke-dashoffset 1s ease';
    setTimeout(() => { ring.style.strokeDashoffset = offset; }, 100);

    // Score color
    if (pct >= 80)      ring.style.stroke = '#22863a';
    else if (pct >= 50) ring.style.stroke = '#f0ad4e';
    else                ring.style.stroke = '#c0392b';

    // Message + trophy
    let msg, icon;
    if (pct >= 90)      { msg = 'Hervorragend! Du beherrschst den Stoff ausgezeichnet.'; icon = '🏆'; }
    else if (pct >= 75) { msg = 'Sehr gut! Nur noch wenige Lücken zu schließen.';         icon = '🎓'; }
    else if (pct >= 50) { msg = 'Gut gemacht! Mit etwas Übung wirst du das meistern.';    icon = '📚'; }
    else                { msg = 'Weiter üben! Lies dir den Stoff nochmals durch.';         icon = '💪'; }

    $('score-message').textContent = msg;
    $('trophy-icon').textContent   = icon;

    // Detail list
    const detail = $('results-detail');
    detail.innerHTML = '';
    state.questions.forEach((q, i) => {
        const ans  = state.answers[i] || { correct: null, userAnswer: '–' };
        const cls  = ans.correct === true  ? 'correct-r'
                   : ans.correct === false ? 'wrong-r' : 'self-r';
        const icon = ans.correct === true  ? '✓' : '✗';

        const item = document.createElement('div');
        item.className = `result-item ${cls}`;
        item.innerHTML = `
            <div class="result-item-header">
                <span class="result-num">${i + 1}.</span>
                <span class="result-q">${escHtml(q.question)}</span>
                <span class="result-status">${icon}</span>
            </div>
            <div class="result-detail">
                ${q.type === 'short_answer'
                    ? `Deine Antwort: ${escHtml(String(ans.userAnswer))}`
                    : `Richtige Antwort: ${escHtml(getCorrectAnswerLabel(q))}`}
                ${q.explanation ? `<br><em>${escHtml(q.explanation)}</em>` : ''}
            </div>
        `;
        detail.appendChild(item);
    });
}

function getCorrectAnswerLabel(q) {
    if (q.type === 'multiple_choice') return q.options[q.correct];
    if (q.type === 'true_false')      return q.correct ? 'Wahr' : 'Falsch';
    return q.answer;
}

// ── Result Buttons ─────────────────────────────────────────────────────────
$('restart-btn').addEventListener('click', () => {
    state.pdfFile    = null;
    state.questions  = [];
    state.answers    = [];
    state.score      = 0;
    $('file-info').classList.add('hidden');
    $('file-input').value = '';
    uploadZone.style.display = '';
    showScreen('setup');
    updateStartBtn();
});

$('retry-btn').addEventListener('click', () => {
    state.currentIndex = 0;
    state.score        = 0;
    state.answers      = [];
    showScreen('quiz');
    renderQuestion(0);
});

// ── Utilities ─────────────────────────────────────────────────────────────
function escHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}
