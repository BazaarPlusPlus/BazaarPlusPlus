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

function DetailsDialog({
  selection,
  locale,
  close
}: {
  selection: Selection;
  locale: Locale;
  close: () => void;
}) {
  const { cards, tagLabels, loading } = useCardText(locale);
  const copy = getSiteCopy(locale).account;
  const definition = selection.templateId
    ? cards?.[selection.templateId.toLowerCase()]
    : undefined;
  return (
    <Dialog labelledBy="card-detail-title" onClose={close}>
      <DialogCard>
        <DialogBody>
          <CardDetail
            card={selection.card}
            definition={definition}
            tagLabels={tagLabels}
            labels={
              loading
                ? { ...copy.cardDetail, unavailable: copy.loading }
                : copy.cardDetail
            }
            tierLabels={copy.tiers}
            enchantLabels={copy.enchantments}
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
