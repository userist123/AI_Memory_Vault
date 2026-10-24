'use client';
// Cartuș de desen arhitectural conform ISO 7200 / SR EN ISO 7200 și standardelor OAR
// Conține: date birou/proiectant, beneficiar, denumire obiectiv, faza proiectare, titlu planșă, scară, dată, revizie, număr planșă și semnături/roluri.
import React from 'react';
import type { Lang } from '@/lib/i18n';
import { translator } from '@/lib/i18n';

export interface ArchitecturalTitleBlockProps {
  studioName?: string | null;
  studioDetails?: string | null;
  studioLogo?: string | null;
  projectName?: string | null;
  clientName?: string | null;
  projectLocation?: string | null;
  projectPhase?: string | null;
  drawingTitle?: string | null;
  sheetNumber?: string | number | null;
  scale?: string | null;
  revision?: string | number | null;
  date?: string | null;
  projectLeader?: string;
  designedBy?: string;
  drawnBy?: string;
  checkedBy?: string;
  locale?: Lang;
  compact?: boolean;
  className?: string;
}

export function ArchitecturalTitleBlock({
  studioName,
  studioDetails,
  studioLogo,
  projectName,
  clientName,
  projectLocation,
  projectPhase,
  drawingTitle,
  sheetNumber,
  scale = '1:50',
  revision = '0',
  date,
  projectLeader = 'Arh. Principal',
  designedBy = 'Casa3D CAD',
  drawnBy = 'Casa3D CAD',
  checkedBy = 'Arh. Verificator',
  locale = 'ro',
  compact = false,
  className = '',
}: ArchitecturalTitleBlockProps) {
  const t = translator(locale);
  const sName = studioName || t('tb.defaultStudio');
  const pName = projectName || t('tb.defaultProject');
  const dTitle = drawingTitle || t('tb.defaultDrawing');
  const phase = projectPhase || t('tb.phaseInterior');
  const d = date || new Date().toLocaleDateString(locale === 'ro' ? 'ro-RO' : 'en-US');

  return (
    <div
      className={`arch-title-block ${compact ? 'arch-tb-compact' : ''} ${className}`.trim()}
      role="region"
      aria-label={t('tb.title')}
    >
      <div className="arch-tb-grid">
        {/* Rândul 1: Studio & Beneficiar */}
        <div className="arch-tb-cell arch-tb-studio">
          <span className="arch-tb-lbl">{t('tb.office')}</span>
          <div className="arch-tb-studio-body">
            {studioLogo && <img src={studioLogo} alt="" className="arch-tb-logo" />}
            <div>
              <div className="arch-tb-name">{sName}</div>
              {studioDetails && <div className="arch-tb-details">{studioDetails}</div>}
            </div>
          </div>
        </div>
        <div className="arch-tb-cell arch-tb-client">
          <span className="arch-tb-lbl">{t('tb.client')}</span>
          <div className="arch-tb-val arch-tb-val-bold">{clientName || '—'}</div>
          {projectLocation && <div className="arch-tb-details">{projectLocation}</div>}
        </div>

        {/* Rândul 2: Obiectiv / Proiect & Faza */}
        <div className="arch-tb-cell arch-tb-proj">
          <span className="arch-tb-lbl">{t('tb.project')}</span>
          <div className="arch-tb-val arch-tb-val-lg">{pName}</div>
        </div>
        <div className="arch-tb-cell arch-tb-phase">
          <span className="arch-tb-lbl">{t('tb.phase')}</span>
          <div className="arch-tb-val">{phase}</div>
        </div>

        {/* Rândul 3: Roluri/Echipă + Titlu Planșă + Date Tehnice Planșă */}
        <div className="arch-tb-team-meta">
          <div className="arch-tb-signatures">
            <div className="arch-tb-sig-row">
              <span className="arch-tb-lbl-inline">{t('tb.lead')}:</span>
              <span className="arch-tb-val-inline">{projectLeader}</span>
            </div>
            <div className="arch-tb-sig-row">
              <span className="arch-tb-lbl-inline">{t('tb.designed')}:</span>
              <span className="arch-tb-val-inline">{designedBy}</span>
            </div>
            <div className="arch-tb-sig-row">
              <span className="arch-tb-lbl-inline">{t('tb.checked')}:</span>
              <span className="arch-tb-val-inline">{checkedBy}</span>
            </div>
          </div>

          <div className="arch-tb-drawing">
            <span className="arch-tb-lbl">{t('tb.drawing')}</span>
            <div className="arch-tb-val arch-tb-val-lg">{dTitle}</div>
          </div>

          <div className="arch-tb-sheet-meta">
            <div className="arch-tb-meta-box">
              <span className="arch-tb-lbl">{t('tb.scale')}</span>
              <span className="arch-tb-val">{scale}</span>
            </div>
            <div className="arch-tb-meta-box">
              <span className="arch-tb-lbl">{t('tb.date')}</span>
              <span className="arch-tb-val">{d}</span>
            </div>
            <div className="arch-tb-meta-box">
              <span className="arch-tb-lbl">{t('tb.rev')}</span>
              <span className="arch-tb-val">{revision}</span>
            </div>
            <div className="arch-tb-meta-box arch-tb-sheet-num">
              <span className="arch-tb-lbl">{t('tb.sheetNo')}</span>
              <span className="arch-tb-val arch-tb-val-xl">
                {sheetNumber != null ? sheetNumber : <i className="tb-n" />}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

export default ArchitecturalTitleBlock;
