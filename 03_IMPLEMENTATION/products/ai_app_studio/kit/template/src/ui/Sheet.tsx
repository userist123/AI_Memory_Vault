import { useId, useRef, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { useModal } from "./modal";

export interface SheetProps {
  open: boolean;
  title: string;
  onClose: () => void;
  children?: ReactNode;
}

/** Bottom sheet (phone-first): slides up, pads the bottom safe area, Escape and the backdrop close it. */
export function Sheet({ open, title, onClose, children }: SheetProps) {
  const panel = useRef<HTMLDivElement>(null);
  const id = useId();
  useModal(open, onClose, panel);
  if (!open) return null;
  return createPortal(
    <div className="ui-overlay ui-overlay--bottom" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div ref={panel} className="ui-sheet" role="dialog" aria-modal="true" aria-labelledby={`${id}-title`} tabIndex={-1}>
        <span className="ui-sheet__handle" aria-hidden="true" />
        <div className="ui-sheet__header">
          <h2 id={`${id}-title`} className="ui-sheet__title">
            {title}
          </h2>
          <button type="button" className="ui-icon-btn" aria-label="Close" onClick={onClose}>
            {"\u00d7"}
          </button>
        </div>
        <div className="ui-sheet__body">{children}</div>
      </div>
    </div>,
    document.body,
  );
}
