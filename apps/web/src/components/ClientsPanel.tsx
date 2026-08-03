import { useState } from 'react';
import { createClientProfile, updateClientProfile } from '../services/api';
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
        <h2>Khách hàng</h2>
      </div>

      <form className="client-form" onSubmit={handleCreate}>
        <input
          type="text"
          placeholder="Tên khách hàng"
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
        <input
          type="text"
          placeholder="Mã số thuế (tuỳ chọn)"
          value={taxCode}
          onChange={(event) => setTaxCode(event.target.value)}
        />
        <select value={clientType} onChange={(event) => setClientType(event.target.value as ClientType)}>
          {clientTypes.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <input
          type="text"
          placeholder="Địa chỉ (tuỳ chọn)"
          value={address}
          onChange={(event) => setAddress(event.target.value)}
        />
        <button type="submit" disabled={saving}>
          Thêm khách hàng
        </button>
      </form>

      {error && <p className="message error">{error}</p>}

      {clientProfiles.length === 0 ? (
        <p className="empty-state">Chưa có khách hàng nào.</p>
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
                  <td>{client.name}</td>
                  <td>{client.taxCode ?? '—'}</td>
                  <td>{clientTypes.find((t) => t.value === client.clientType)?.label ?? client.clientType}</td>
                  <td>{client.address ?? '—'}</td>
                  <td>
                    <input
                      type="checkbox"
                      checked={client.isActive}
                      onChange={() => handleToggleActive(client)}
                      aria-label={`Toggle active for ${client.name}`}
                    />
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
