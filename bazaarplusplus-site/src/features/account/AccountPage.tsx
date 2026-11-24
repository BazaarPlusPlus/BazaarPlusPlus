import type { AccountRoutePage, ResolvedSpaLocation } from '../../app/router';
import AuthPage from './AuthPage';
import ProfilePage from './ProfilePage';
import RecordsPage from './RecordsPage';

type Props = {
  location: ResolvedSpaLocation;
  replacePage: (page: AccountRoutePage) => void;
};

export default function AccountPage(props: Props) {
  switch (props.location.route.page) {
    case 'account':
      return <ProfilePage {...props} />;
    case 'records':
      return <RecordsPage {...props} />;
    default:
      return <AuthPage {...props} />;
  }
}
