import { useState } from 'react';
import { createClientProfile, updateClientProfile } from '../services/api';
import { Alert } from './Alert';
import { EmptyState } from './EmptyState';
import { UsersIcon } from './icons';
import type { ClientProfileDto, ClientType } from '../types';

interface Props {
  clientProfiles: ClientProfileDto[];
  onChanged: () => void;
}

const clientTypes: { value: ClientType; label: string }[] = [
  { value: 'HouseholdBusiness', label: 'Hộ kinh doanh' },
  { value: 'Enterprise', label: 'Doanh nghiệp' },
  { value: 'Individual', label: 'Cá nhân' },
];

export function ClientsPanel({ clientProfiles, onChanged }: Props) {
  const [name, setName] = useState('');
  const [taxCode, setTaxCode] = useState('');
  const [clientType, setClientType] = useState<ClientType>('HouseholdBusiness');
  const [address, setAddress] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const handleCreate = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim()) {
      setError('Tên khách hàng là bắt buộc.');
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await createClientProfile({
        name: name.trim(),
        taxCode: taxCode.trim() || null,
        clientType,
        address: address.trim() || null,
      });
      setName('');
      setTaxCode('');
      setAddress('');
      setClientType('HouseholdBusiness');
      onChanged();
    } catch (err) {
      const message =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Không thể tạo khách hàng.';
      setError(message);
    } finally {
      setSaving(false);
    }
  };

  const handleToggleActive = async (client: ClientProfileDto) => {
    await updateClientProfile(client.id, {
      name: client.name,
      taxCode: client.taxCode,
      clientType: client.clientType,
      address: client.address,
      isActive: !client.isActive,
    });
    onChanged();
  };

  return (
    <section className="panel">
      <div className="section-heading">
        <h2>
          <UsersIcon size={18} />
          Khách hàng
        </h2>
        <span className="chip">{clientProfiles.length} khách hàng</span>
      </div>

      <form className="client-form" onSubmit={handleCreate}>
        <label className="field">
          Tên khách hàng
          <input
            type="text"
            placeholder="Tên khách hàng"
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        </label>
        <label className="field">
          Mã số thuế
          <input
            type="text"
            placeholder="Tuỳ chọn"
            value={taxCode}
            onChange={(event) => setTaxCode(event.target.value)}
          />
        </label>
        <label className="field">
          Loại khách hàng
          <select value={clientType} onChange={(event) => setClientType(event.target.value as ClientType)}>
            {clientTypes.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          Địa chỉ
          <input
            type="text"
            placeholder="Tuỳ chọn"
            value={address}
            onChange={(event) => setAddress(event.target.value)}
          />
        </label>
        <button type="submit" className="btn-primary" disabled={saving}>
          Thêm khách hàng
        </button>
      </form>

      {error && <Alert variant="error">{error}</Alert>}

      {clientProfiles.length === 0 ? (
        <EmptyState
          icon={<UsersIcon size={22} />}
          title="Chưa có khách hàng nào"
          description="Thêm khách hàng để lọc và tự động gán tài liệu theo mã số thuế."
        />
      ) : (
        <div className="table-wrap">
          <table className="documents-table">
            <thead>
              <tr>
                <th>Tên</th>
                <th>Mã số thuế</th>
                <th>Loại</th>
                <th>Địa chỉ</th>
                <th>Đang hoạt động</th>
              </tr>
            </thead>
            <tbody>
              {clientProfiles.map((client) => (
                <tr key={client.id}>
                  <td className="file-name">{client.name}</td>
                  <td>{client.taxCode ?? '—'}</td>
                  <td>{clientTypes.find((t) => t.value === client.clientType)?.label ?? client.clientType}</td>
                  <td>{client.address ?? '—'}</td>
                  <td>
                    <label className="switch">
                      <input
                        type="checkbox"
                        checked={client.isActive}
                        onChange={() => handleToggleActive(client)}
                        aria-label={`Toggle active for ${client.name}`}
                      />
                      <span className="switch-track" />
                    </label>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
