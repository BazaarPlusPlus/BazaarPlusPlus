import { createContext, useContext, useState, type ReactNode } from 'react';
import {
  Button,
  CardDetail,
  Dialog,
  DialogBody,
  DialogCard,
  DialogFooter,
  type GameCard
} from '@bpp/ui';
import type { Locale } from '../../app/router';
import { getSiteCopy } from '../../content/site-copy';
import { useCardText } from './card-text';

type Selection = { templateId: string | null; card: GameCard };
const SelectionContext = createContext<
  ((selection: Selection) => void) | undefined
>(undefined);

export function useOpenCard() {
  return useContext(SelectionContext);
}

/** A card's Card Text definition, read lazily; the dialog and the hover preview share it. */
export function CardDefinition({
  templateId,
  card,
  locale,
  titleId,
  preview = false
}: {
  templateId: string | null;
  card: GameCard;
  locale: Locale;
  titleId?: string;
  preview?: boolean;
}) {
  const { cards, tagLabels, loading } = useCardText(locale);
  const copy = getSiteCopy(locale).account;
  return (
    <CardDetail
      card={card}
      definition={templateId ? cards?.[templateId.toLowerCase()] : undefined}
      tagLabels={tagLabels}
      labels={
        loading
          ? { ...copy.cardDetail, unavailable: copy.loading }
          : copy.cardDetail
      }
      tierLabels={copy.tiers}
      enchantLabels={copy.enchantments}
      titleId={titleId}
      preview={preview}
    />
  );
}

function DetailsDialog({
  selection,
  locale,
  close
}: {
  selection: Selection;
  locale: Locale;
  close: () => void;
}) {
  const copy = getSiteCopy(locale).account;
  return (
    <Dialog labelledBy="card-detail-title" onClose={close}>
      <DialogCard>
        <DialogBody>
          <CardDefinition
            templateId={selection.templateId}
            card={selection.card}
            locale={locale}
            titleId="card-detail-title"
          />
        </DialogBody>
        <DialogFooter>
          <Button onClick={close}>{copy.cardDetail.close}</Button>
        </DialogFooter>
      </DialogCard>
    </Dialog>
  );
}

export function CardDetails({
  children,
  locale
}: {
  children: ReactNode;
  locale: Locale;
}) {
  const [selection, setSelection] = useState<Selection | null>(null);
  return (
    <SelectionContext.Provider value={setSelection}>
      {children}
      {selection && (
        <DetailsDialog
          selection={selection}
          locale={locale}
          close={() => setSelection(null)}
        />
      )}
    </SelectionContext.Provider>
  );
}
