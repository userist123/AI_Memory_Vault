import { Column, Entity, Index, PrimaryColumn } from 'typeorm';

@Entity('auth_local_session_accounts')
export class LocalAuthAccountEntity {
  @PrimaryColumn({ type: 'varchar', length: 36 })
  id!: string;

  @Index({ unique: true })
  @Column({ type: 'varchar', length: 255 })
  username!: string;

  @Column({ type: 'text' })
  passwordHash!: string;

  @Column({ type: 'text' })
  rolesJson!: string;

  @Column({ type: 'text' })
  permissionsJson!: string;

  @Column({ type: 'boolean' })
  mustChangePassword!: boolean;

  @Column({ type: 'bigint' })
  createdAt!: number;

  @Column({ type: 'bigint' })
  updatedAt!: number;

  @Column({ type: 'bigint', nullable: true })
  disabledAt!: number | null;

  @Column({ type: 'bigint', nullable: true })
  lockoutUntil!: number | null;
}
