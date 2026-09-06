'use client';

import { useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { Button } from '@/components/ui/button';
import { useAudioRecorder } from '@/hooks/useAudioRecorder';
import type { JournalExtraction } from '@/types/journal';
import {
  Mic,
  Square,
  Pause,
  Play,
  Trash2,
  Save,
  Loader2,
  AlertCircle,
  CheckCircle2,
  Volume2,
  Sparkles,
} from 'lucide-react';
import { cn } from '@/lib/utils';

type ProcessingStage = 'idle' | 'transcribing' | 'analyzing' | 'saving' | 'done' | 'error';

export function VoiceJournal() {
  const locale = useLocale() as 'ro' | 'en';
  const t = useTranslations('journal');
  const tCommon = useTranslations('common');

  const recorder = useAudioRecorder();
  const [processingStage, setProcessingStage] = useState<ProcessingStage>('idle');
  const [transcript, setTranscript] = useState<string>('');
  const [extraction, setExtraction] = useState<JournalExtraction | null>(null);
  const [processingError, setProcessingError] = useState<string | null>(null);
  const [savedId, setSavedId] = useState<string | null>(null);

  const formatDuration = (sec: number) => {
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    return `${m}:${s.toString().padStart(2, '0')}`;
  };

  const handleProcess = async () => {
    if (!recorder.audioBlob) return;

    setProcessingError(null);
    setTranscript('');
    setExtraction(null);
    setSavedId(null);

    try {
      // 1. Transcribe
      setProcessingStage('transcribing');
      const formData = new FormData();
      formData.append('audio', recorder.audioBlob, 'recording.webm');
      formData.append('language', locale);

      const transcribeRes = await fetch('/api/voice/transcribe', {
        method: 'POST',
        body: formData,
      });

      if (!transcribeRes.ok) {
        const err = await transcribeRes.json();
        throw new Error(err.error || 'Transcription failed');
      }

      const transcribeData = await transcribeRes.json();
      setTranscript(transcribeData.transcript);

      // 2. Analyze
      setProcessingStage('analyzing');
      const analyzeRes = await fetch('/api/voice/analyze', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          transcript: transcribeData.transcript,
          language: locale,
        }),
      });

      if (!analyzeRes.ok) {
        const err = await analyzeRes.json();
        throw new Error(err.error || 'Analysis failed');
      }

      const analyzeData = await analyzeRes.json();
      setExtraction(analyzeData.extraction);

      // 3. Save
      setProcessingStage('saving');
      const saveRes = await fetch('/api/journal/entries', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          transcript: transcribeData.transcript,
          language: locale,
          extraction: analyzeData.extraction,
          audioDurationSec: recorder.durationSec,
          tags: [],
        }),
      });

      if (!saveRes.ok) {
        const err = await saveRes.json();
        throw new Error(err.error || 'Save failed');
      }

      const saveData = await saveRes.json();
      setSavedId(saveData.id);
      setProcessingStage('done');
    } catch (err: unknown) {
      const e = err as { message?: string };
      console.error('[VoiceJournal] Process error:', e);
      setProcessingError(e.message || 'Unknown error');
      setProcessingStage('error');
    }
  };

  const handleReset = () => {
    recorder.reset();
    setProcessingStage('idle');
    setTranscript('');
    setExtraction(null);
    setProcessingError(null);
    setSavedId(null);
  };

  const isProcessing =
    processingStage === 'transcribing' ||
    processingStage === 'analyzing' ||
    processingStage === 'saving';

  return (
    <div className="space-y-6">
      {/* Recording Panel */}
      <div className="rounded-xl border border-border bg-card p-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div
              className={cn(
                'flex h-12 w-12 items-center justify-center rounded-lg transition-colors',
                recorder.status === 'recording'
                  ? 'bg-loss/10 text-loss animate-pulse'
                  : 'bg-primary/10 text-primary'
              )}
            >
              <Mic className="h-6 w-6" />
            </div>
            <div>
              <h3 className="text-lg font-semibold">{t('voiceJournal')}</h3>
              <p className="text-sm text-muted-foreground">
                {recorder.status === 'recording' && t('recording')}
                {recorder.status === 'paused' &&
                  (locale === 'ro' ? 'Pauză' : 'Paused')}
                {recorder.status === 'stopped' &&
                  formatDuration(recorder.durationSec)}
                {(recorder.status === 'idle' || recorder.status === 'requesting') &&
                  (locale === 'ro'
                    ? 'Apasă pentru a înregistra (max 25 MB, ~30 min)'
                    : 'Press to record (max 25 MB, ~30 min)')}
              </p>
            </div>
          </div>

          {recorder.status === 'recording' && (
            <div className="flex items-center gap-2">
              <div className="h-3 w-3 rounded-full bg-loss animate-pulse" />
              <span className="font-mono text-lg font-semibold">
                {formatDuration(recorder.durationSec)}
              </span>
            </div>
          )}
        </div>

        {/* Audio waveform placeholder - could be enhanced with visualizer */}
        {recorder.status === 'recording' && (
          <div className="mt-4 flex h-12 items-center justify-center gap-1 rounded-lg bg-muted/30 px-4">
            {Array.from({ length: 40 }).map((_, i) => (
              <div
                key={i}
                className="w-1 bg-primary/60 animate-pulse"
                style={{
                  height: `${20 + Math.random() * 60}%`,
                  animationDelay: `${i * 50}ms`,
                  animationDuration: '0.8s',
                }}
              />
            ))}
          </div>
        )}

        {/* Controls */}
        <div className="mt-6 flex flex-wrap gap-2">
          {recorder.status === 'idle' && (
            <Button onClick={recorder.start} size="lg" className="gap-2">
              <Mic className="h-4 w-4" />
              {t('recordVoice')}
            </Button>
          )}

          {recorder.status === 'requesting' && (
            <Button disabled size="lg">
              <Loader2 className="h-4 w-4 animate-spin" />
              {locale === 'ro' ? 'Se solicită acces...' : 'Requesting access...'}
            </Button>
          )}

          {recorder.status === 'recording' && (
            <>
              <Button onClick={recorder.pause} variant="outline" size="lg" className="gap-2">
                <Pause className="h-4 w-4" />
                {locale === 'ro' ? 'Pauză' : 'Pause'}
              </Button>
              <Button onClick={recorder.stop} variant="destructive" size="lg" className="gap-2">
                <Square className="h-4 w-4" />
                {t('stopRecording')}
              </Button>
            </>
          )}

          {recorder.status === 'paused' && (
            <>
              <Button onClick={recorder.resume} size="lg" className="gap-2">
                <Play className="h-4 w-4" />
                {locale === 'ro' ? 'Continuă' : 'Resume'}
              </Button>
              <Button onClick={recorder.stop} variant="destructive" size="lg" className="gap-2">
                <Square className="h-4 w-4" />
                {t('stopRecording')}
              </Button>
            </>
          )}

          {recorder.status === 'stopped' && (
            <>
              {recorder.audioUrl && (
                <audio
                  controls
                  src={recorder.audioUrl}
                  className="h-10 flex-1 min-w-[200px]"
                />
              )}
              <Button
                onClick={handleProcess}
                disabled={isProcessing}
                size="lg"
                className="gap-2"
              >
                {isProcessing ? (
                  <Loader2 className="h-4 w-4 animate-spin" />
                ) : (
                  <Sparkles className="h-4 w-4" />
                )}
                {processingStage === 'transcribing' && t('transcribing')}
                {processingStage === 'analyzing' && t('analyzing')}
                {processingStage === 'saving' &&
                  (locale === 'ro' ? 'Se salvează...' : 'Saving...')}
                {!isProcessing &&
                  (locale === 'ro' ? 'Transcrie și analizează' : 'Transcribe & Analyze')}
              </Button>
              <Button onClick={handleReset} variant="ghost" size="lg" className="gap-2">
                <Trash2 className="h-4 w-4" />
                {tCommon('delete')}
              </Button>
            </>
          )}
        </div>

        {/* Recorder error */}
        {recorder.error && (
          <div className="mt-4 flex items-start gap-2 rounded-lg border border-destructive/50 bg-destructive/10 p-4 text-sm">
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
            <div>
              <p className="font-medium text-destructive">{tCommon('error')}</p>
              <p className="mt-1 text-muted-foreground">{recorder.error}</p>
            </div>
          </div>
        )}
      </div>

      {/* Processing error */}
      {processingError && (
        <div className="rounded-xl border border-destructive/50 bg-destructive/10 p-6">
          <div className="flex items-start gap-3">
            <AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-destructive" />
            <div className="flex-1">
              <p className="font-semibold text-destructive">
                {locale === 'ro' ? 'Eroare la procesare' : 'Processing error'}
              </p>
              <p className="mt-1 text-sm text-muted-foreground">{processingError}</p>
              {processingError.includes('GROQ_API_KEY') && (
                <p className="mt-2 text-xs text-muted-foreground">
                  💡 {locale === 'ro'
                    ? 'Obține cheia gratuită de la console.groq.com și adaug-o în .env.local'
                    : 'Get a free API key from console.groq.com and add it to .env.local'}
                </p>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Transcript */}
      {transcript && (
        <div className="rounded-xl border border-border bg-card p-6">
          <div className="mb-3 flex items-center gap-2">
            <Volume2 className="h-4 w-4 text-muted-foreground" />
            <h4 className="text-sm font-semibold text-muted-foreground">
              {locale === 'ro' ? 'Transcriere' : 'Transcript'}
            </h4>
          </div>
          <p className="whitespace-pre-wrap text-sm leading-relaxed">{transcript}</p>
        </div>
      )}

      {/* Extraction */}
      {extraction && (
        <div className="rounded-xl border border-primary/30 bg-primary/5 p-6">
          <div className="mb-4 flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-primary" />
            <h4 className="text-sm font-semibold text-primary">
              {t('aiInsights')}
            </h4>
            <span className="ml-auto text-xs text-muted-foreground">
              {locale === 'ro' ? 'Încredere' : 'Confidence'}:{' '}
              {Math.round(extraction.confidence * 100)}%
            </span>
          </div>

          <p className="mb-4 text-sm font-medium">{extraction.summary}</p>

          <div className="grid gap-4 sm:grid-cols-2">
            {extraction.instrument && (
              <ExtractionField
                label={locale === 'ro' ? 'Instrument' : 'Instrument'}
                value={extraction.instrument}
              />
            )}
            {extraction.direction && (
              <ExtractionField
                label={locale === 'ro' ? 'Direcție' : 'Direction'}
                value={
                  extraction.direction === 'long'
                    ? locale === 'ro'
                      ? 'Long (cumpărare)'
                      : 'Long'
                    : locale === 'ro'
                      ? 'Short (vânzare)'
                      : 'Short'
                }
                valueClass={
                  extraction.direction === 'long' ? 'text-profit' : 'text-loss'
                }
              />
            )}
            {extraction.setup && (
              <ExtractionField
                label={t('setup')}
                value={extraction.setup}
                fullWidth
              />
            )}
            {extraction.rMultipleEstimate !== null && (
              <ExtractionField
                label="R-multiple"
                value={`${extraction.rMultipleEstimate > 0 ? '+' : ''}${extraction.rMultipleEstimate}R`}
                valueClass={
                  extraction.rMultipleEstimate > 0 ? 'text-profit' : 'text-loss'
                }
              />
            )}
          </div>

          {extraction.emotions.length > 0 && (
            <div className="mt-4">
              <p className="mb-2 text-xs font-semibold uppercase text-muted-foreground">
                {locale === 'ro' ? 'Emoții' : 'Emotions'}
              </p>
              <div className="flex flex-wrap gap-2">
                {extraction.emotions.map((e) => (
                  <span
                    key={e}
                    className="rounded-full border border-border bg-background px-3 py-1 text-xs font-medium"
                  >
                    {t(`emotions.${e}`)}
                  </span>
                ))}
              </div>
            </div>
          )}

          {extraction.mistakes.length > 0 && (
            <div className="mt-4">
              <p className="mb-2 text-xs font-semibold uppercase text-muted-foreground">
                {locale === 'ro' ? 'Greșeli identificate' : 'Mistakes identified'}
              </p>
              <div className="flex flex-wrap gap-2">
                {extraction.mistakes.map((m) => (
                  <span
                    key={m}
                    className="rounded-full border border-loss/30 bg-loss/10 px-3 py-1 text-xs font-medium text-loss"
                  >
                    {t(`mistakes.${m}`)}
                  </span>
                ))}
              </div>
            </div>
          )}

          {extraction.lesson && (
            <div className="mt-4 rounded-lg border border-primary/20 bg-background p-4">
              <p className="mb-1 text-xs font-semibold uppercase text-primary">
                {t('lesson')}
              </p>
              <p className="text-sm">{extraction.lesson}</p>
            </div>
          )}
        </div>
      )}

      {/* Saved confirmation */}
      {savedId && (
        <div className="flex items-center gap-3 rounded-xl border border-profit/30 bg-profit/5 p-4">
          <CheckCircle2 className="h-5 w-5 text-profit" />
          <p className="text-sm">
            <span className="font-semibold text-profit">
              {locale === 'ro' ? 'Salvat cu succes' : 'Saved successfully'}
            </span>
            <span className="ml-2 text-muted-foreground">
              {locale === 'ro'
                ? 'Intrare de jurnal creată'
                : 'Journal entry created'}
            </span>
          </p>
          <Button
            onClick={handleReset}
            variant="ghost"
            size="sm"
            className="ml-auto"
          >
            {locale === 'ro' ? 'Înregistrare nouă' : 'New recording'}
          </Button>
        </div>
      )}
    </div>
  );
}

function ExtractionField({
  label,
  value,
  fullWidth = false,
  valueClass,
}: {
  label: string;
  value: string;
  fullWidth?: boolean;
  valueClass?: string;
}) {
  return (
    <div className={cn(fullWidth && 'sm:col-span-2')}>
      <p className="text-xs font-semibold uppercase text-muted-foreground">
        {label}
      </p>
      <p className={cn('mt-1 text-sm font-medium', valueClass)}>{value}</p>
    </div>
  );
}
