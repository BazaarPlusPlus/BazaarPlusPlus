import {
  createContext,
  useContext,
  useState,
  type ReactNode,
  type SyntheticEvent
} from 'react';
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

function WarmCardText({ locale }: { locale: Locale }) {
  useCardText(locale);
  return null;
}

export function CardDetails({
  children,
  locale
}: {
  children: ReactNode;
  locale: Locale;
}) {
  const [selection, setSelection] = useState<Selection | null>(null);
  // Card Text holds every card, so fetch it once a card that opens is first pointed at or focused.
  const [warm, setWarm] = useState(false);
  const approach = (event: SyntheticEvent) => {
    if (
      !warm &&
      event.target instanceof Element &&
      event.target.closest(
        'button.bpp-card, button.bpp-skill-tile, button.bpp-card-chip'
      )
    )
      setWarm(true);
  };
  return (
    <SelectionContext.Provider value={setSelection}>
      <div
        style={{ display: 'contents' }}
        onPointerOver={approach}
        onFocus={approach}
      >
        {children}
      </div>
      {warm && <WarmCardText locale={locale} />}
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
