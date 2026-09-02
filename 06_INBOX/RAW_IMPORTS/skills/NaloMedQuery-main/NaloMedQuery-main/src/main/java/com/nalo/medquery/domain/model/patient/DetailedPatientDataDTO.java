package com.nalo.medquery.domain.model.patient;

import com.nalo.medquery.domain.entity.PatientEntity;

public record DetailedPatientDataDTO(
    Long id,
    String nome,
    String email,
    String telefone,
    String cpf
) {
    public DetailedPatientDataDTO(PatientEntity patientEntity) {
        this(
            patientEntity.getId(), patientEntity.getNome(), 
            patientEntity.getEmail(), patientEntity.getTelefone(), 
            patientEntity.getCpf()
        );
    }
}
