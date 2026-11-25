import {
  Button,
  Dialog,
  DialogCard,
  DialogHeader,
  DialogBody,
  DialogFooter
} from '@bpp/ui';
import { translate, type DemoProps } from '../copy';
import { useState } from 'react';
export default function Demo({ lang }: DemoProps) {
  const t = (zh: string, en: string) => translate(lang, zh, en);
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button onClick={() => setOpen(true)}>
        {t('查看详情', 'View details')}
      </Button>
      {open && (
        <Dialog labelledBy="detail-title" onClose={() => setOpen(false)}>
          <DialogCard>
            <DialogHeader
              titleId="detail-title"
              title={t('本地记录', 'Local history')}
              onClose={() => setOpen(false)}
              closeLabel={t('关闭', 'Close')}
            />
            <DialogBody>
              {t('此局已保存。', 'This run has been saved.')}
            </DialogBody>
            <DialogFooter>
              <Button onClick={() => setOpen(false)}>
                {t('完成', 'Done')}
              </Button>
            </DialogFooter>
          </DialogCard>
        </Dialog>
      )}
    </>
  );
}
