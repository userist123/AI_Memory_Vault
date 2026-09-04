package com.nalo.medquery.domain.model.doctor;

import com.nalo.medquery.domain.entity.DoctorEntity;

public record DetailedDoctorDataDTO(
    Long id,
    String nome,
    String email,
    String telefone,
    String crm,
    EnumDoctorSpecialty specialty
) {
    public DetailedDoctorDataDTO(DoctorEntity doctorEntity) {
        this(
            doctorEntity.getId(), doctorEntity.getNome(), 
            doctorEntity.getEmail(), doctorEntity.getTelefone(), 
            doctorEntity.getCrm(), doctorEntity.getEspecialidade()
        );
    }
}