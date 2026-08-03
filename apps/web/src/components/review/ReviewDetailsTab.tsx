import type { ReviewField, ReviewSection, ReviewWarningDto } from '../../types';

type FieldControl = HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

interface Props {
  sections: ReviewSection[];
  values: Record<string, string>;
  onFieldChange: (fieldKey: string, value: string) => void;
  warningsByFieldKey: Map<string, ReviewWarningDto[]>;
  showDebug: boolean;
  registerFieldRef: (fieldKey: string, el: FieldControl | null) => void;
}

function isIsoDate(value: string | null): boolean {
  return !!value && /^\d{4}-\d{2}-\d{2}$/.test(value);
}

function fieldInputType(field: ReviewField, value: string): 'textarea' | 'select' | 'date' | 'text' {
  if (field.dataType === 'MultilineText') return 'textarea';
  if ((field.dataType === 'Enum' || field.dataType === 'Currency') && field.options && field.options.length > 0) {
    return 'select';
  }
  if (field.dataType === 'Date' && isIsoDate(value)) return 'date';
  return 'text';
}

function isWideField(field: ReviewField, inputType: string): boolean {
  return inputType === 'textarea' || /address|note/i.test(field.fieldKey);
}

export function ReviewDetailsTab({
  sections,
  values,
  onFieldChange,
  warningsByFieldKey,
  showDebug,
  registerFieldRef,
}: Props) {
  return (
    <>
      {sections
        .slice()
        .sort((a, b) => a.displayOrder - b.displayOrder)
        .map((section) => (
          <div key={section.sectionKey} className="review-section">
            <h3>{section.title}</h3>
            {section.description && <p className="muted">{section.description}</p>}

            <div className="field-grid">
              {section.fields
                .slice()
                .sort((a, b) => a.displayOrder - b.displayOrder)
                .map((field) => {
                  const fieldWarnings = warningsByFieldKey.get(field.fieldKey) ?? [];
                  const value = values[field.fieldKey] ?? '';
                  const inputType = fieldInputType(field, value);
                  const missingRequired = field.isMissing && field.isRequired;

                  return (
                    <label
                      key={field.fieldKey}
                      className={[
                        'field-editor',
                        fieldWarnings.length > 0 ? 'has-warning' : '',
                        missingRequired ? 'is-missing-required' : '',
                        isWideField(field, inputType) ? 'is-wide' : '',
                      ]
                        .filter(Boolean)
                        .join(' ')}
                    >
                      <span className="field-label-row">
                        <span>
                          {field.label}
                          {field.isRequired ? ' *' : ''}
                        </span>
                        <span className="field-meta">
                          {field.confidence !== null ? `${Math.round(field.confidence * 100)}%` : 'No confidence'}
                          {field.isEditedByUser ? ' · edited' : ''}
                        </span>
                      </span>

                      {inputType === 'textarea' ? (
                        <textarea
                          ref={(el) => registerFieldRef(field.fieldKey, el)}
                          value={value}
                          onChange={(event) => onFieldChange(field.fieldKey, event.target.value)}
                        />
                      ) : inputType === 'select' ? (
                        <select
                          ref={(el) => registerFieldRef(field.fieldKey, el)}
                          value={value}
                          onChange={(event) => onFieldChange(field.fieldKey, event.target.value)}
                        >
                          <option value="">—</option>
                          {field.options?.map((option) => (
                            <option key={option} value={option}>
                              {option}
                            </option>
                          ))}
                        </select>
                      ) : (
                        <input
                          ref={(el) => registerFieldRef(field.fieldKey, el)}
                          type={inputType === 'date' ? 'date' : 'text'}
                          value={value}
                          onChange={(event) => onFieldChange(field.fieldKey, event.target.value)}
                        />
                      )}

                      {fieldWarnings.map((warning, index) => (
                        <span key={index} className="field-warning">
                          {warning.message}
                        </span>
                      ))}

                      {showDebug && (field.sourceText || field.sourceType || field.extractionMethod) && (
                        <span className="field-debug">
                          {[field.sourceType, field.extractionMethod, field.sourcePageNumber ? `p.${field.sourcePageNumber}` : null]
                            .filter(Boolean)
                            .join(' · ')}
                          {field.sourceText ? ` — "${field.sourceText}"` : ''}
                        </span>
                      )}
                    </label>
                  );
                })}
            </div>
          </div>
        ))}
    </>
  );
}
