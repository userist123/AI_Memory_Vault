package com.nalo.medquery.domain.entity;

import com.nalo.medquery.domain.model.doctor.DoctorRegisterData;
import com.nalo.medquery.domain.model.doctor.EnumDoctorSpecialty;
import com.nalo.medquery.domain.model.doctor.DoctorUpdatableData;

import jakarta.persistence.Column;
import jakarta.persistence.Embedded;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import lombok.AllArgsConstructor;
import lombok.EqualsAndHashCode;
import lombok.Getter;
import lombok.NoArgsConstructor;


@Table(name = "doctors")
@Entity(name = "doctor")
@Getter
@NoArgsConstructor
@AllArgsConstructor
@EqualsAndHashCode( of = "id" )
public class DoctorEntity {
    
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, columnDefinition = "TINYINT")
    private Boolean active;
    private String nome;
    private String email;
    private String telefone;
    private String crm;
    @Enumerated(EnumType.STRING)
    @Column(name = "especialidade", length = 100)
    private EnumDoctorSpecialty especialidade;
    @Embedded
    private AddressEntity endereco;

    public DoctorEntity(DoctorRegisterData data){
        this.active = true;
        this.nome = data.nome();
        this.email = data.email();
        this.telefone = data.telefone();
        this.crm = data.crm();
        this.especialidade = data.especialidade();
        this.endereco = new AddressEntity(data.endereco());
    }

    public void updateDoctorInformation(DoctorUpdatableData data){
        if (data.nome() != null){
            this.nome = data.nome();
        }
        if(data.telefone() != null){
            this.telefone = data.telefone();
        }
        if (data.endereco() != null) {
            this.endereco.updateAddressInformation(data.endereco());
        }
    }

    public void setInactive() {
        this.active = false;
    }
}


