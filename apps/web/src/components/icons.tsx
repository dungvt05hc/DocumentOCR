import type { ReactNode } from 'react';

interface IconProps {
  size?: number;
  className?: string;
}

function base(paths: ReactNode, { size = 18, className }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
    >
      {paths}
    </svg>
  );
}

export const UploadIcon = (props: IconProps) =>
  base(
    <>
      <path d="M12 16V4" />
      <path d="M6 10l6-6 6 6" />
      <path d="M4 16v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3" />
    </>,
    props
  );

export const DocumentsIcon = (props: IconProps) =>
  base(
    <>
      <path d="M8 3h5l5 5v11a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Z" />
      <path d="M13 3v5h5" />
      <path d="M9 13h6M9 17h6" />
    </>,
    props
  );

export const UsersIcon = (props: IconProps) =>
  base(
    <>
      <circle cx="9" cy="8" r="3.2" />
      <path d="M3.5 19a5.6 5.6 0 0 1 11 0" />
      <path d="M16 8.2a3.2 3.2 0 1 1 3.6 3.16" />
      <path d="M20.4 19a5 5 0 0 0-3.6-4.8" />
    </>,
    props
  );

export const DownloadIcon = (props: IconProps) =>
  base(
    <>
      <path d="M12 4v12" />
      <path d="M6 12l6 6 6-6" />
      <path d="M4 20h16" />
    </>,
    props
  );

export const CheckIcon = (props: IconProps) => base(<path d="M4 12.5l5 5L20 6" />, props);

export const CheckCircleIcon = (props: IconProps) =>
  base(
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M8 12.5l2.7 2.7L16 9.5" />
    </>,
    props
  );

export const AlertTriangleIcon = (props: IconProps) =>
  base(
    <>
      <path d="M12 3.5 21.5 20h-19L12 3.5Z" />
      <path d="M12 9.5v4.5" />
      <path d="M12 17h.01" />
    </>,
    props
  );

export const AlertCircleIcon = (props: IconProps) =>
  base(
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7.5v5.5" />
      <path d="M12 16.5h.01" />
    </>,
    props
  );

export const InfoIcon = (props: IconProps) =>
  base(
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 10.5v6" />
      <path d="M12 7.5h.01" />
    </>,
    props
  );

export const ChevronLeftIcon = (props: IconProps) => base(<path d="M15 5l-7 7 7 7" />, props);

export const ChevronRightIcon = (props: IconProps) => base(<path d="M9 5l7 7-7 7" />, props);

export const RefreshIcon = (props: IconProps) =>
  base(
    <>
      <path d="M4 12a8 8 0 0 1 14-5.3L21 9" />
      <path d="M21 4v5h-5" />
      <path d="M20 12a8 8 0 0 1-14 5.3L3 15" />
      <path d="M3 20v-5h5" />
    </>,
    props
  );

export const ClockIcon = (props: IconProps) =>
  base(
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3.5 2" />
    </>,
    props
  );

export const SunIcon = (props: IconProps) =>
  base(
    <>
      <circle cx="12" cy="12" r="4.2" />
      <path d="M12 2.5v2.4M12 19.1v2.4M4.6 4.6l1.7 1.7M17.7 17.7l1.7 1.7M2.5 12h2.4M19.1 12h2.4M4.6 19.4l1.7-1.7M17.7 6.3l1.7-1.7" />
    </>,
    props
  );

export const MoonIcon = (props: IconProps) =>
  base(<path d="M20 14.5A8.5 8.5 0 0 1 9.5 4 8.5 8.5 0 1 0 20 14.5Z" />, props);

export const FileTypeIcon = (props: IconProps) =>
  base(
    <>
      <path d="M8 3h5l5 5v11a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Z" />
      <path d="M13 3v5h5" />
    </>,
    props
  );

export const SearchIcon = (props: IconProps) =>
  base(
    <>
      <circle cx="11" cy="11" r="7" />
      <path d="M21 21l-4.3-4.3" />
    </>,
    props
  );

export const InboxIcon = (props: IconProps) =>
  base(
    <>
      <path d="M4 12h4l2 3h4l2-3h4" />
      <path d="M5.5 6h13l1.5 6v6a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-6l1.5-6Z" />
    </>,
    props
  );

export const PlayIcon = (props: IconProps) =>
  base(
    <>
      <path d="M6 4.5v15l13-7.5-13-7.5Z" />
    </>,
    props
  );

export const SaveIcon = (props: IconProps) =>
  base(
    <>
      <path d="M5 4h11l3 3v13H5V4Z" />
      <path d="M8 4v5h8V4" />
      <path d="M8 14h8v6H8v-6Z" />
    </>,
    props
  );
