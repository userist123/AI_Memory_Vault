import { useId, useRef, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { useModal } from "./modal";

export interface DialogProps {
  open: boolean;
  title: string;
  description?: ReactNode;
  onClose: () => void;
  /** buttons; put the safe choice first */
  actions?: ReactNode;
  children?: ReactNode;
}

/** Centred modal dialog (confirmations, short forms). Escape and the backdrop close it. */
export function Dialog({ open, title, description, onClose, actions, children }: DialogProps) {
  const panel = useRef<HTMLDivElement>(null);
  const id = useId();
  useModal(open, onClose, panel);
  if (!open) return null;
  return createPortal(
    <div className="ui-overlay ui-overlay--center" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        ref={panel}
        className="ui-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${id}-title`}
        aria-describedby={description ? `${id}-desc` : undefined}
        tabIndex={-1}
      >
        <h2 id={`${id}-title`} className="ui-dialog__title">
          {title}
        </h2>
        {description ? (
          <p id={`${id}-desc`} className="ui-dialog__desc">
            {description}
          </p>
        ) : null}
        {children}
        {actions ? <div className="ui-dialog__actions">{actions}</div> : null}
      </div>
    </div>,
    document.body,
  );
}
