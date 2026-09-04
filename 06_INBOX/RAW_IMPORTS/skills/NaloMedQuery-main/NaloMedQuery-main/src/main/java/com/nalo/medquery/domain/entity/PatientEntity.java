package com.nalo.medquery.domain.entity;


import com.nalo.medquery.domain.model.patient.PatientRegisterData;
import com.nalo.medquery.domain.model.patient.PatientUpdatableData;

import jakarta.persistence.Column;
import jakarta.persistence.Embedded;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import lombok.AllArgsConstructor;
import lombok.EqualsAndHashCode;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Table(name = "patients")
@Entity(name = "patient")
@Getter
@NoArgsConstructor
@AllArgsConstructor
@EqualsAndHashCode( of = "id" )
public class PatientEntity {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(nullable = false, columnDefinition = "TINYINT")
    private Boolean ativo;
    private String nome;
    private String email;
    private String telefone;
    private String cpf;
    
    @Embedded
    private AddressEntity endereco;

    public PatientEntity(PatientRegisterData data){
        this.ativo = true;
        this.nome = data.nome();
        this.email = data.email();
        this.telefone = data.telefone();
        this.cpf = data.cpf();        
        this.endereco = new AddressEntity(data.endereco());
    }

    public void updatePatientInformation(PatientUpdatableData data){
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
        this.ativo = false;
    }
}
